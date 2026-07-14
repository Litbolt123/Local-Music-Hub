using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LocalMusicHub.Models;

namespace LocalMusicHub.Services;

/// <summary>
/// Lyrics from local sidecars, app cache, or <see href="https://lrclib.net">LRCLIB</see> (not archive.org).
/// Successful online fetches are saved next to the audio file when possible.
/// </summary>
public static class LyricsService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly Regex LrcLineRegex = new(
        @"\[(\d{1,2}):(\d{2})(?:[.:](\d{1,3}))?\](.*)",
        RegexOptions.Compiled);

    public static string CacheDirectory => Path.Combine(AppPaths.DataDirectory, "LyricsCache");

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LocalMusicHub/0.9.6 (local music player; +https://lrclib.net)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public static bool HasLocalLyrics(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var audioPath = CuePathHelper.ResolveAudioPath(path);
        var isCueVirtual = !string.Equals(audioPath, path, StringComparison.OrdinalIgnoreCase);

        if (TryReadEmbeddedLyrics(audioPath) is not null)
            return true;
        if (TryReadAppCache(path) is not null)
            return true;
        if (!isCueVirtual && TryReadLocalLyrics(audioPath) is not null)
            return true;
        return false;
    }

    public static async Task<LyricsResult> GetLyricsAsync(
        LibraryTrack track,
        CancellationToken cancellationToken = default,
        bool persistOnlineResult = true)
    {
        var embedded = TryReadEmbeddedLyrics(track.AudioFilePath);
        if (embedded is not null)
            return embedded;

        var local = TryReadLocalLyrics(track.AudioFilePath);
        if (local is not null)
            return local;

        var cached = TryReadAppCache(track.FilePath);
        if (cached is not null)
            return cached;

        var online = await FetchOnlineAsync(track, cancellationToken).ConfigureAwait(false);
        if (persistOnlineResult && online.Found)
            TryPersist(track.FilePath, online);
        return online;
    }

    /// <summary>Fetch and cache if missing. Returns whether lyrics were newly saved.</summary>
    public static async Task<bool> PrefetchAsync(LibraryTrack track, bool force = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(track.FilePath))
            return false;
        if (!force && HasLocalLyrics(track.FilePath))
            return false;

        var result = await FetchOnlineAsync(track, cancellationToken).ConfigureAwait(false);
        if (!result.Found)
        {
            if (!force || !HasLocalLyrics(track.FilePath))
                LyricsNotFoundStore.Mark(track.FilePath);
            return false;
        }

        LyricsNotFoundStore.Clear(track.FilePath);
        TryPersist(track.FilePath, result, overwrite: force);
        return true;
    }

    private static async Task<LyricsResult> FetchOnlineAsync(LibraryTrack track, CancellationToken cancellationToken)
    {
        try
        {
            var artist = Uri.EscapeDataString(track.DisplayArtist);
            var title = Uri.EscapeDataString(track.DisplayTitle);
            var album = Uri.EscapeDataString(track.DisplayAlbum);
            var duration = Math.Max(1, (int)Math.Round(track.Duration.TotalSeconds));
            var url =
                $"https://lrclib.net/api/get?artist_name={artist}&track_name={title}&album_name={album}&duration={duration}";

            using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                url = $"https://lrclib.net/api/search?artist_name={artist}&track_name={title}";
                using var search = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!search.IsSuccessStatusCode)
                    return LyricsResult.NotFound("No lyrics found online (LRCLIB).");

                await using var searchStream = await search.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var searchDoc = await JsonDocument.ParseAsync(searchStream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (searchDoc.RootElement.ValueKind != JsonValueKind.Array || searchDoc.RootElement.GetArrayLength() == 0)
                    return LyricsResult.NotFound("No lyrics found online (LRCLIB).");

                var first = searchDoc.RootElement[0];
                var plain = first.TryGetProperty("plainLyrics", out var p) ? p.GetString() : null;
                var synced = first.TryGetProperty("syncedLyrics", out var s) ? s.GetString() : null;
                return FromOnline(plain, synced);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;
            var plainLyrics = root.TryGetProperty("plainLyrics", out var plainProp) ? plainProp.GetString() : null;
            var syncedLyrics = root.TryGetProperty("syncedLyrics", out var syncedProp) ? syncedProp.GetString() : null;
            return FromOnline(plainLyrics, syncedLyrics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return LyricsResult.NotFound($"Could not fetch lyrics: {ex.Message}");
        }
    }

    private static LyricsResult FromOnline(string? plain, string? synced)
    {
        var timed = ParseLrc(synced);
        if (timed.Count > 0)
        {
            var text = string.Join(Environment.NewLine, timed.Select(l => l.Text));
            return LyricsResult.Ok(text, "LRCLIB (synced)", timed, synced?.Trim());
        }

        if (!string.IsNullOrWhiteSpace(plain))
            return LyricsResult.Ok(plain.Trim(), "LRCLIB");

        if (!string.IsNullOrWhiteSpace(synced))
            return LyricsResult.Ok(StripLrcTimestamps(synced).Trim(), "LRCLIB", rawLrc: synced.Trim());

        return LyricsResult.NotFound("Lyrics entry was empty.");
    }

    public static void TryPersist(string storagePath, LyricsResult result, bool overwrite = false)
    {
        if (!result.Found || string.IsNullOrWhiteSpace(storagePath))
            return;

        var body = !string.IsNullOrWhiteSpace(result.RawLrc)
            ? result.RawLrc!
            : result.Text;
        if (string.IsNullOrWhiteSpace(body))
            return;

        var preferLrc = !string.IsNullOrWhiteSpace(result.RawLrc) || result.IsSynced;
        var ext = preferLrc ? ".lrc" : ".txt";
        var audioPath = CuePathHelper.ResolveAudioPath(storagePath);
        var isCueVirtual = !string.Equals(audioPath, storagePath, StringComparison.OrdinalIgnoreCase);

        // Prefer sidecar next to the audio file (whole-file tracks only).
        if (!isCueVirtual)
        {
            try
            {
                var dir = Path.GetDirectoryName(audioPath);
                var stem = Path.GetFileNameWithoutExtension(audioPath);
                if (!string.IsNullOrWhiteSpace(dir) && !string.IsNullOrWhiteSpace(stem) && Directory.Exists(dir))
                {
                    var sidecar = Path.Combine(dir, stem + ext);
                    if (overwrite || !File.Exists(sidecar))
                        File.WriteAllText(sidecar, body.Trim() + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                /* read-only folder, etc. */
            }
        }

        if (App.Settings.EmbedLyricsInTags && !isCueVirtual)
        {
            try
            {
                // Embed plain lyrics; synced LRC remains in sidecar/cache for highlighting.
                var plain = preferLrc ? StripLrcTimestamps(body) : body.Trim();
                if (!string.IsNullOrWhiteSpace(plain))
                    AudioTagWriter.TryWriteLyrics(audioPath, plain);
            }
            catch
            {
                /* ignore tag write failures */
            }
        }

        // Always keep an app-data fallback cache.
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var cachePath = GetAppCachePath(storagePath, preferLrc);
            if (overwrite || !File.Exists(cachePath))
                File.WriteAllText(cachePath, body.Trim() + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            /* ignore */
        }
    }

    private static LyricsResult? TryReadEmbeddedLyrics(string audioPath)
    {
        var text = AudioTagWriter.TryReadLyrics(audioPath);
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return LyricsResult.Ok(text.Trim(), "Embedded tag");
    }

    private static LyricsResult? TryReadLocalLyrics(string audioPath)
    {
        var dir = Path.GetDirectoryName(audioPath);
        var stem = Path.GetFileNameWithoutExtension(audioPath);
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(stem))
            return null;

        foreach (var ext in new[] { ".lrc", ".txt" })
        {
            var candidate = Path.Combine(dir, stem + ext);
            if (!File.Exists(candidate))
                continue;

            try
            {
                var text = File.ReadAllText(candidate);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (ext.Equals(".lrc", StringComparison.OrdinalIgnoreCase))
                {
                    var timed = ParseLrc(text);
                    if (timed.Count > 0)
                    {
                        var plain = string.Join(Environment.NewLine, timed.Select(l => l.Text));
                        return LyricsResult.Ok(plain, $"Local ({Path.GetFileName(candidate)})", timed, text.Trim());
                    }

                    text = StripLrcTimestamps(text);
                }

                return LyricsResult.Ok(text.Trim(), $"Local ({Path.GetFileName(candidate)})");
            }
            catch
            {
                /* ignore unreadable sidecar */
            }
        }

        return null;
    }

    private static LyricsResult? TryReadAppCache(string audioPath)
    {
        try
        {
            foreach (var preferLrc in new[] { true, false })
            {
                var path = GetAppCachePath(audioPath, preferLrc);
                if (!File.Exists(path))
                    continue;

                var text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (preferLrc)
                {
                    var timed = ParseLrc(text);
                    if (timed.Count > 0)
                    {
                        var plain = string.Join(Environment.NewLine, timed.Select(l => l.Text));
                        return LyricsResult.Ok(plain, "Cached (LRCLIB)", timed, text.Trim());
                    }
                }

                return LyricsResult.Ok(text.Trim(), "Cached (LRCLIB)");
            }
        }
        catch
        {
            /* ignore */
        }

        return null;
    }

    private static string GetAppCachePath(string audioPath, bool lrc)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(audioPath.Trim().ToLowerInvariant())))
            .ToLowerInvariant();
        return Path.Combine(CacheDirectory, hash + (lrc ? ".lrc" : ".txt"));
    }

    public static IReadOnlyList<LyricsLine> ParseLrc(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc))
            return [];

        var lines = new List<LyricsLine>();
        foreach (var raw in lrc.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = LrcLineRegex.Match(raw.Trim());
            if (!match.Success)
                continue;

            var minutes = int.Parse(match.Groups[1].Value);
            var seconds = int.Parse(match.Groups[2].Value);
            var frac = match.Groups[3].Success ? match.Groups[3].Value : "0";
            if (frac.Length == 1)
                frac += "00";
            else if (frac.Length == 2)
                frac += "0";
            var ms = int.Parse(frac.PadRight(3, '0')[..3]);
            var text = match.Groups[4].Value.Trim();
            if (text.Length == 0)
                continue;

            lines.Add(new LyricsLine
            {
                Time = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(ms),
                Text = text,
            });
        }

        return lines.OrderBy(l => l.Time).ToList();
    }

    private static string StripLrcTimestamps(string lrc)
    {
        var lines = lrc.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(Environment.NewLine,
            lines.Select(line => Regex.Replace(line, @"\[\d{1,2}:\d{2}([.:]\d{1,3})?\]", "").Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('[')));
    }
}

public sealed class LyricsLine
{
    public TimeSpan Time { get; init; }
    public string Text { get; init; } = "";
}

public sealed class LyricsResult
{
    public bool Found { get; init; }
    public string Text { get; init; } = "";
    public string Source { get; init; } = "";
    public IReadOnlyList<LyricsLine> TimedLines { get; init; } = [];
    public string? RawLrc { get; init; }
    public bool IsSynced => TimedLines.Count > 0;

    public static LyricsResult Ok(
        string text,
        string source,
        IReadOnlyList<LyricsLine>? timed = null,
        string? rawLrc = null) => new()
    {
        Found = true,
        Text = text,
        Source = source,
        TimedLines = timed ?? [],
        RawLrc = rawLrc,
    };

    public static LyricsResult NotFound(string message) => new()
    {
        Found = false,
        Text = message,
        Source = "",
    };
}
