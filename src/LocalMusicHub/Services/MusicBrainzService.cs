using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using LocalMusicHub.Data;
using LocalMusicHub.Models;

namespace LocalMusicHub.Services;

public static class MusicBrainzService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly HttpClient CoverHttp = CreateCoverClient();
    private static DateTime _lastRequestUtc = DateTime.MinValue;
    private static readonly object RateGate = new();
    private static readonly object CoverFetchGate = new();
    private static readonly Dictionary<string, CoverFetchSlot> _coverFetches =
        new(StringComparer.OrdinalIgnoreCase);

    private const int PerUrlTimeoutSeconds = 12;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LocalMusicHub/0.7.8 (https://github.com/local-music-hub)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static HttpClient CreateCoverClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 8 };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LocalMusicHub/0.7.8 (https://github.com/local-music-hub)");
        return client;
    }

    public static async Task<IReadOnlyList<MusicBrainzRecording>> SearchRecordingsAsync(
        string artist,
        string title,
        CancellationToken cancellationToken = default)
    {
        await ThrottleAsync(cancellationToken).ConfigureAwait(false);

        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(title))
            queryParts.Add($"recording:\"{Escape(title)}\"");
        if (!string.IsNullOrWhiteSpace(artist))
            queryParts.Add($"artist:\"{Escape(artist)}\"");
        if (queryParts.Count == 0)
            return [];

        var query = Uri.EscapeDataString(string.Join(" AND ", queryParts));
        var url = $"https://musicbrainz.org/ws/2/recording/?query={query}&fmt=json&limit=12";
        using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("recordings", out var recordings) ||
            recordings.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<MusicBrainzRecording>();
        foreach (var item in recordings.EnumerateArray())
        {
            var recTitle = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var score = item.TryGetProperty("score", out var s) ? s.GetInt32() : 0;
            var artistName = "";
            if (item.TryGetProperty("artist-credit", out var credits) && credits.ValueKind == JsonValueKind.Array)
            {
                var names = credits.EnumerateArray()
                    .Select(c => c.TryGetProperty("name", out var n) ? n.GetString() : null)
                    .Where(n => !string.IsNullOrWhiteSpace(n));
                artistName = string.Join(", ", names!);
            }

            var releaseOptions = ParseReleases(item);
            if (releaseOptions.Count == 0)
            {
                list.Add(new MusicBrainzRecording
                {
                    Title = recTitle,
                    Artist = artistName,
                    Album = "",
                    Year = null,
                    TrackNumber = null,
                    Score = score,
                });
                continue;
            }

            var best = releaseOptions
                .OrderByDescending(r => !string.IsNullOrWhiteSpace(r.ReleaseId))
                .ThenByDescending(r => r.Year ?? 0)
                .First();

            list.Add(new MusicBrainzRecording
            {
                Title = recTitle,
                Artist = artistName,
                Album = best.Album,
                Year = best.Year,
                TrackNumber = best.TrackNumber,
                ReleaseId = best.ReleaseId,
                Score = score,
                AlternateReleases = releaseOptions
                    .Where(r => !string.Equals(r.ReleaseId, best.ReleaseId, StringComparison.OrdinalIgnoreCase) ||
                                !string.Equals(r.Album, best.Album, StringComparison.OrdinalIgnoreCase))
                    .Take(4)
                    .ToList(),
            });
        }

        return list;
    }

    public static async Task<IReadOnlyList<MusicBrainzReleaseHit>> SearchReleasesAsync(
        string artist,
        string album,
        CancellationToken cancellationToken = default)
    {
        await ThrottleAsync(cancellationToken).ConfigureAwait(false);

        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(album))
            queryParts.Add($"release:\"{Escape(album)}\"");
        if (!string.IsNullOrWhiteSpace(artist))
            queryParts.Add($"artist:\"{Escape(artist)}\"");
        if (queryParts.Count == 0)
            return [];

        var query = Uri.EscapeDataString(string.Join(" AND ", queryParts));
        var url = $"https://musicbrainz.org/ws/2/release/?query={query}&fmt=json&limit=12";
        using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("releases", out var releases) ||
            releases.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<MusicBrainzReleaseHit>();
        foreach (var item in releases.EnumerateArray())
        {
            var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var score = item.TryGetProperty("score", out var s) ? s.GetInt32() : 0;
            var artistName = "";
            if (item.TryGetProperty("artist-credit", out var credits) && credits.ValueKind == JsonValueKind.Array)
            {
                var names = credits.EnumerateArray()
                    .Select(c => c.TryGetProperty("name", out var n) ? n.GetString() : null)
                    .Where(n => !string.IsNullOrWhiteSpace(n));
                artistName = string.Join(", ", names!);
            }

            int? year = null;
            if (item.TryGetProperty("date", out var dateProp))
            {
                var date = dateProp.GetString() ?? "";
                if (date.Length >= 4 && int.TryParse(date[..4], out var y))
                    year = y;
            }

            var country = item.TryGetProperty("country", out var cProp) ? cProp.GetString() ?? "" : "";
            list.Add(new MusicBrainzReleaseHit
            {
                Title = title,
                Artist = artistName,
                ReleaseId = id,
                Year = year,
                Country = country,
                Score = score,
            });
        }

        return list;
    }

    /// <summary>
    /// Fetches album cover from Apple iTunes / Deezer CDNs (not Cover Art Archive / archive.org).
    /// </summary>
    public static async Task<CoverArtFetchResult> FetchAlbumCoverAsync(
        string? artist,
        string? album,
        IProgress<CoverArtProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        artist = artist?.Trim() ?? "";
        album = album?.Trim() ?? "";
        if (artist.Length == 0 && album.Length == 0)
            return CoverArtFetchResult.Fail("Need artist or album to look up cover art.");

        var cacheKey = $"{artist}\n{album}".ToLowerInvariant();
        CoverFetchSlot slot;
        lock (CoverFetchGate)
        {
            if (_coverFetches.TryGetValue(cacheKey, out var existing) &&
                !existing.Task.IsFaulted &&
                !existing.Task.IsCanceled)
            {
                slot = existing;
                slot.AddProgress(progress);
                if (slot.LastProgress is { } last)
                    progress?.Report(last);
            }
            else
            {
                slot = new CoverFetchSlot();
                slot.AddProgress(progress);
                slot.Task = FetchAlbumCoverCoreAsync(artist, album, slot.Report, CancellationToken.None);
                _coverFetches[cacheKey] = slot;
                _ = slot.Task.ContinueWith(_ =>
                {
                    lock (CoverFetchGate)
                    {
                        if (_coverFetches.TryGetValue(cacheKey, out var cur) && ReferenceEquals(cur, slot))
                            _coverFetches.Remove(cacheKey);
                    }
                }, TaskScheduler.Default);
            }
        }

        if (!cancellationToken.CanBeCanceled)
            return await slot.Task.ConfigureAwait(false);

        var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var reg = cancellationToken.Register(() => cancelTcs.TrySetResult(true));
        var completed = await Task.WhenAny(slot.Task, cancelTcs.Task).ConfigureAwait(false);
        if (!ReferenceEquals(completed, slot.Task))
            throw new OperationCanceledException(cancellationToken);
        return await slot.Task.ConfigureAwait(false);
    }

    /// <summary>Release id alone cannot fetch covers; use artist/album overload.</summary>
    public static Task<CoverArtFetchResult> FetchCoverArtAsync(
        string? releaseId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(CoverArtFetchResult.Fail(
            "Cover lookup needs artist and album. Use Edit album → Fetch online, or MusicBrainz cover search."));

    public static Task<CoverArtFetchResult> FetchCoverArtAsync(
        string? releaseId,
        IProgress<CoverArtProgress>? progress,
        CancellationToken cancellationToken = default) =>
        FetchCoverArtAsync(releaseId, cancellationToken);

    private static async Task<CoverArtFetchResult> FetchAlbumCoverCoreAsync(
        string artist,
        string album,
        Action<CoverArtProgress> report,
        CancellationToken cancellationToken)
    {
        report(new CoverArtProgress("Searching Apple Music for cover…", 0, 3));
        var urls = new List<(string Url, string Source)>();

        try
        {
            var itunes = await FindItunesCoverUrlsAsync(artist, album, cancellationToken).ConfigureAwait(false);
            urls.AddRange(itunes.Select(u => (u, "Apple")));
            report(new CoverArtProgress(
                itunes.Count > 0 ? $"Apple: {itunes.Count} cover URL(s)" : "Apple: no match",
                1,
                3));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            report(new CoverArtProgress($"Apple search failed: {ShortError(ex)}", 1, 3));
        }

        report(new CoverArtProgress("Searching Deezer for cover…", 2, 3));
        try
        {
            var deezer = await FindDeezerCoverUrlsAsync(artist, album, cancellationToken).ConfigureAwait(false);
            urls.AddRange(deezer.Select(u => (u, "Deezer")));
            report(new CoverArtProgress(
                deezer.Count > 0 ? $"Deezer: {deezer.Count} cover URL(s)" : "Deezer: no match",
                2,
                3));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            report(new CoverArtProgress($"Deezer search failed: {ShortError(ex)}", 2, 3));
        }

        // Prefer Last.fm album art when the user already connected an API key.
        try
        {
            var lastFm = await FindLastFmCoverUrlsAsync(artist, album, cancellationToken).ConfigureAwait(false);
            urls.AddRange(lastFm.Select(u => (u, "Last.fm")));
        }
        catch
        {
            /* optional */
        }

        urls = urls
            .Where(u => !string.IsNullOrWhiteSpace(u.Url) && !IsArchiveOrgUrl(u.Url))
            .GroupBy(u => u.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (urls.Count == 0)
        {
            report(new CoverArtProgress("No cover URLs found (Apple/Deezer).", 3, 3));
            return CoverArtFetchResult.Fail(
                "No cover found on Apple Music or Deezer. Use Edit album → Choose image…");
        }

        Exception? lastError = null;
        for (var i = 0; i < urls.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (url, source) = urls[i];
            report(new CoverArtProgress(
                $"Downloading {i + 1}/{urls.Count} ({source})…",
                i + 1,
                urls.Count));

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(PerUrlTimeoutSeconds));
                var bytes = await DownloadImageAsync(url, timeoutCts.Token).ConfigureAwait(false);
                if (bytes is { Length: > 0 })
                {
                    bytes = CoverArtHelper.NormalizeDownloadedCover(bytes) ?? bytes;
                    report(new CoverArtProgress(
                        $"Downloaded from {source} ({bytes.Length / 1024} KB)",
                        urls.Count,
                        urls.Count));
                    return CoverArtFetchResult.Ok(bytes, coverKnown: true);
                }

                report(new CoverArtProgress($"Empty response ({source}), trying next…", i + 1, urls.Count));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = new TimeoutException($"Timed out after {PerUrlTimeoutSeconds}s ({source})");
                report(new CoverArtProgress(
                    $"Timed out on {source} ({PerUrlTimeoutSeconds}s), trying next…",
                    i + 1,
                    urls.Count));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                report(new CoverArtProgress($"Failed ({source}), trying next…", i + 1, urls.Count));
            }
        }

        report(new CoverArtProgress("All cover downloads failed.", urls.Count, urls.Count));
        return CoverArtFetchResult.Fail(FriendlyCoverError(lastError)) with { CoverKnown = true };
    }

    private static async Task<IReadOnlyList<string>> FindItunesCoverUrlsAsync(
        string artist,
        string album,
        CancellationToken cancellationToken)
    {
        var term = Uri.EscapeDataString($"{artist} {album}".Trim());
        var url = $"https://itunes.apple.com/search?term={term}&entity=album&limit=8";
        using var response = await CoverHttp.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return [];

        var urls = new List<string>();
        foreach (var item in results.EnumerateArray())
        {
            var art = item.TryGetProperty("artworkUrl100", out var a) ? a.GetString() : null;
            if (string.IsNullOrWhiteSpace(art))
                continue;

            // Upsize common iTunes thumb sizes.
            var large = art
                .Replace("100x100bb", "600x600bb")
                .Replace("60x60bb", "600x600bb");
            urls.Add(large);
            if (!string.Equals(large, art, StringComparison.Ordinal))
                urls.Add(art);
        }

        return urls;
    }

    private static async Task<IReadOnlyList<string>> FindDeezerCoverUrlsAsync(
        string artist,
        string album,
        CancellationToken cancellationToken)
    {
        var q = Uri.EscapeDataString($"{artist} {album}".Trim());
        var url = $"https://api.deezer.com/search/album?q={q}&limit=8";
        using var response = await CoverHttp.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        var urls = new List<string>();
        foreach (var item in data.EnumerateArray())
        {
            foreach (var key in new[] { "cover_xl", "cover_big", "cover_medium", "cover" })
            {
                if (item.TryGetProperty(key, out var c) && c.ValueKind == JsonValueKind.String)
                {
                    var u = c.GetString();
                    if (!string.IsNullOrWhiteSpace(u))
                        urls.Add(u);
                }
            }
        }

        return urls;
    }

    private static async Task<IReadOnlyList<string>> FindLastFmCoverUrlsAsync(
        string artist,
        string album,
        CancellationToken cancellationToken)
    {
        var settings = App.Settings;
        if (string.IsNullOrWhiteSpace(settings.LastFmApiKey))
            return [];

        var url =
            "https://ws.audioscrobbler.com/2.0/?method=album.getinfo" +
            $"&api_key={Uri.EscapeDataString(settings.LastFmApiKey)}" +
            $"&artist={Uri.EscapeDataString(artist)}" +
            $"&album={Uri.EscapeDataString(album)}" +
            "&format=json";

        using var response = await CoverHttp.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return [];

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("album", out var alb) ||
            !alb.TryGetProperty("image", out var images) ||
            images.ValueKind != JsonValueKind.Array)
            return [];

        var urls = new List<string>();
        foreach (var img in images.EnumerateArray().Reverse())
        {
            var u = img.TryGetProperty("#text", out var t) ? t.GetString() : null;
            if (!string.IsNullOrWhiteSpace(u) && !IsArchiveOrgUrl(u))
                urls.Add(u);
        }

        return urls;
    }

    private static bool IsArchiveOrgUrl(string url) =>
        url.Contains("archive.org", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("coverartarchive.org", StringComparison.OrdinalIgnoreCase);

    private static async Task<byte[]?> DownloadImageAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        using var response = await CoverHttp.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return bytes is { Length: > 0 } ? bytes : null;
    }

    private static string FriendlyCoverError(Exception? ex)
    {
        var msg = ex?.Message ?? "";
        if (ex is TaskCanceledException or TimeoutException ||
            msg.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            return "Cover download timed out. Try another release, or Edit album → Choose image…";
        }

        return string.IsNullOrWhiteSpace(msg)
            ? "Could not download cover art."
            : $"Cover fetch failed: {msg}";
    }

    private static string ShortError(Exception ex)
    {
        var msg = ex.Message;
        return msg.Length <= 60 ? msg : msg[..57] + "…";
    }

    public static async Task<bool> ApplyRecordingToTrackAsync(
        LibraryRepository repository,
        LibraryTrack track,
        string recordingId,
        CancellationToken cancellationToken = default)
    {
        await ThrottleAsync(cancellationToken).ConfigureAwait(false);
        var url = $"https://musicbrainz.org/ws/2/recording/{Uri.EscapeDataString(recordingId)}?inc=artist-credits+releases&fmt=json";
        using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return false;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var recording = doc.RootElement;
        var title = recording.TryGetProperty("title", out var titleNode) ? titleNode.GetString() ?? track.Title : track.Title;
        var artist = "";
        if (recording.TryGetProperty("artist-credit", out var credits) && credits.ValueKind == JsonValueKind.Array &&
            credits.GetArrayLength() > 0 &&
            credits[0].TryGetProperty("name", out var creditName))
        {
            artist = creditName.GetString() ?? track.Artist;
        }

        var releases = ParseReleases(recording);
        var primary = releases.FirstOrDefault();
        var match = new MusicBrainzRecording
        {
            Title = title,
            Artist = string.IsNullOrWhiteSpace(artist) ? track.Artist : artist,
            Album = primary?.Album ?? track.Album,
            Year = primary?.Year ?? track.Year,
            TrackNumber = primary?.TrackNumber ?? track.TrackNumber,
            ReleaseId = primary?.ReleaseId,
            Score = 100,
            AlternateReleases = releases,
        };

        byte[]? cover = null;
        if (!string.IsNullOrWhiteSpace(match.Album))
        {
            var coverResult = await FetchAlbumCoverAsync(match.Artist, match.Album, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            cover = coverResult.Bytes;
        }

        var updated = ApplyToTrack(track, match, cover);
        repository.UpsertTrack(updated);
        AudioTagWriter.Write(updated);
        if (cover is { Length: > 0 })
            CoverArtHelper.WriteCoverToFile(updated.AudioFilePath, CoverArtHelper.EncodeJpegSquare(cover, outputSize: 600) ?? cover);
        return true;
    }

    public static LibraryTrack ApplyToTrack(
        LibraryTrack source,
        MusicBrainzRecording match,
        byte[]? coverArt = null) => new()
    {
        Id = source.Id,
        FilePath = source.FilePath,
        Title = string.IsNullOrWhiteSpace(match.Title) ? source.Title : match.Title,
        Artist = string.IsNullOrWhiteSpace(match.Artist) ? source.Artist : match.Artist,
        AlbumArtist = string.IsNullOrWhiteSpace(match.Artist) ? source.AlbumArtist : match.Artist,
        Album = string.IsNullOrWhiteSpace(match.Album) ? source.Album : match.Album,
        TrackNumber = match.TrackNumber ?? source.TrackNumber,
        Year = match.Year ?? source.Year,
        Genre = source.Genre,
        Duration = source.Duration,
        Bitrate = source.Bitrate,
        Format = source.Format,
        DateAddedUtc = source.DateAddedUtc,
        FileModifiedUtc = DateTime.UtcNow,
        CoverArt = coverArt is { Length: > 0 } ? coverArt : source.CoverArt,
        PlayCount = source.PlayCount,
        LastPlayedUtc = source.LastPlayedUtc,
        Rating = source.Rating,
        ReplayGainTrackDb = source.ReplayGainTrackDb,
        ReplayGainAlbumDb = source.ReplayGainAlbumDb,
        ReplayGainTrackPeak = source.ReplayGainTrackPeak,
        ReplayGainAlbumPeak = source.ReplayGainAlbumPeak,
        CueStartMs = source.CueStartMs,
        CueEndMs = source.CueEndMs,
        ReviewStatus = source.ReviewStatus,
    };

    private static List<MusicBrainzReleaseInfo> ParseReleases(JsonElement recording)
    {
        var list = new List<MusicBrainzReleaseInfo>();
        if (!recording.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var release in releases.EnumerateArray())
        {
            var album = release.TryGetProperty("title", out var at) ? at.GetString() ?? "" : "";
            var releaseId = release.TryGetProperty("id", out var id) ? id.GetString() : null;
            int? year = null;
            if (release.TryGetProperty("date", out var dateProp))
            {
                var date = dateProp.GetString() ?? "";
                if (date.Length >= 4 && int.TryParse(date[..4], out var y))
                    year = y;
            }

            int? trackNumber = null;
            if (release.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Array &&
                media.GetArrayLength() > 0 &&
                media[0].TryGetProperty("track", out var tracks) && tracks.ValueKind == JsonValueKind.Array &&
                tracks.GetArrayLength() > 0 &&
                tracks[0].TryGetProperty("number", out var numProp) &&
                int.TryParse(numProp.GetString(), out var tn))
            {
                trackNumber = tn;
            }

            list.Add(new MusicBrainzReleaseInfo(album, releaseId, year, trackNumber));
        }

        return list;
    }

    private static async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        TimeSpan wait;
        lock (RateGate)
        {
            var since = DateTime.UtcNow - _lastRequestUtc;
            wait = since < TimeSpan.FromSeconds(1.1) ? TimeSpan.FromSeconds(1.1) - since : TimeSpan.Zero;
            _lastRequestUtc = DateTime.UtcNow + wait;
        }

        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
    }

    private static string Escape(string value) => value.Replace("\"", "\\\"");

    private sealed class CoverFetchSlot
    {
        private readonly object _gate = new();
        private readonly List<IProgress<CoverArtProgress>> _listeners = [];
        public Task<CoverArtFetchResult> Task { get; set; } = null!;
        public CoverArtProgress? LastProgress { get; private set; }

        public void AddProgress(IProgress<CoverArtProgress>? progress)
        {
            if (progress is null)
                return;
            lock (_gate)
                _listeners.Add(progress);
        }

        public void Report(CoverArtProgress value)
        {
            LastProgress = value;
            List<IProgress<CoverArtProgress>> copy;
            lock (_gate)
                copy = _listeners.ToList();
            foreach (var p in copy)
            {
                try { p.Report(value); }
                catch { /* ignore */ }
            }
        }
    }
}

public sealed record CoverArtProgress(string Message, int Current = 0, int Total = 0)
{
    public double Fraction => Total > 0 ? Math.Clamp(Current / (double)Total, 0, 1) : 0;
}

public sealed class CoverArtDiscovery
{
    public static CoverArtDiscovery None { get; } = new(false, []);

    public CoverArtDiscovery(bool found, IReadOnlyList<string> urls)
    {
        Found = found;
        Urls = urls;
    }

    public bool Found { get; }
    public IReadOnlyList<string> Urls { get; }
}

public sealed record CoverArtFetchResult(byte[]? Bytes, string? Error, bool CoverKnown = false)
{
    public bool Succeeded => Bytes is { Length: > 0 };

    public static CoverArtFetchResult Ok(byte[] bytes, bool coverKnown = true) =>
        new(bytes, null, coverKnown);

    public static CoverArtFetchResult Fail(string error) => new(null, error, false);
}

public sealed record MusicBrainzReleaseInfo(string Album, string? ReleaseId, int? Year, int? TrackNumber);

public sealed class MusicBrainzReleaseHit
{
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public string? ReleaseId { get; init; }
    public int? Year { get; init; }
    public string Country { get; init; } = "";
    public int Score { get; init; }

    public string DisplayLabel
    {
        get
        {
            var year = Year is > 0 ? $" ({Year})" : "";
            var country = string.IsNullOrWhiteSpace(Country) ? "" : $" · {Country}";
            return $"{Artist} — {Title}{year}{country}  ({Score}%)";
        }
    }
}

public sealed class MusicBrainzRecording
{
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public string Album { get; init; } = "";
    public int? Year { get; init; }
    public int? TrackNumber { get; init; }
    public string? ReleaseId { get; init; }
    public int Score { get; init; }
    public IReadOnlyList<MusicBrainzReleaseInfo> AlternateReleases { get; init; } = [];

    public string DisplayLabel
    {
        get
        {
            var baseLabel = string.IsNullOrWhiteSpace(Album)
                ? $"{Artist} — {Title}  ({Score}%)"
                : $"{Artist} — {Title} · {Album}  ({Score}%)";
            if (AlternateReleases.Count > 0)
                baseLabel += $" · +{AlternateReleases.Count} other release(s)";
            return baseLabel;
        }
    }

    public MusicBrainzRecording WithRelease(MusicBrainzReleaseInfo release) => new()
    {
        Title = Title,
        Artist = Artist,
        Album = release.Album,
        Year = release.Year ?? Year,
        TrackNumber = release.TrackNumber ?? TrackNumber,
        ReleaseId = release.ReleaseId,
        Score = Score,
        AlternateReleases = [],
    };
}
