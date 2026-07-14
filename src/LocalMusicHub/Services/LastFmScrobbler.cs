using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalMusicHub.Models;

namespace LocalMusicHub.Services;

public sealed class LastFmScrobbler
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly object _gate = new();
    private long? _pendingTrackId;
    private DateTime _pendingStartedUtc;
    private bool _scrobbledCurrent;

    public bool IsConfigured(AppSettings settings) =>
        settings.LastFmScrobbleEnabled &&
        !string.IsNullOrWhiteSpace(settings.LastFmApiKey) &&
        !string.IsNullOrWhiteSpace(settings.LastFmApiSecret) &&
        !string.IsNullOrWhiteSpace(settings.LastFmSessionKey);

    public void OnTrackStarted(LibraryTrack track)
    {
        lock (_gate)
        {
            _pendingTrackId = track.Id;
            _pendingStartedUtc = DateTime.UtcNow;
            _scrobbledCurrent = false;
        }

        _ = UpdateNowPlayingAsync(track);
    }

    public void OnPositionTick(LibraryTrack? track, TimeSpan position, TimeSpan duration, AppSettings settings)
    {
        if (!IsConfigured(settings) || track is null || track.Id <= 0)
            return;

        lock (_gate)
        {
            if (_scrobbledCurrent || _pendingTrackId != track.Id)
                return;

            var elapsed = DateTime.UtcNow - _pendingStartedUtc;
            var enoughTime = elapsed.TotalSeconds >= 30 ||
                             (duration.TotalSeconds > 0 && position.TotalSeconds >= duration.TotalSeconds * 0.5);
            if (!enoughTime)
                return;

            _scrobbledCurrent = true;
        }

        _ = ScrobbleAsync(track, settings);
    }

    private async Task UpdateNowPlayingAsync(LibraryTrack track)
    {
        var settings = App.Settings;
        if (!IsConfigured(settings))
            return;

        try
        {
            await CallAsync(settings, new Dictionary<string, string>
            {
                ["method"] = "track.updateNowPlaying",
                ["artist"] = track.DisplayArtist,
                ["track"] = track.DisplayTitle,
                ["album"] = track.DisplayAlbum,
            }).ConfigureAwait(false);
        }
        catch
        {
            /* ignore */
        }
    }

    private async Task ScrobbleAsync(LibraryTrack track, AppSettings settings)
    {
        try
        {
            await CallAsync(settings, new Dictionary<string, string>
            {
                ["method"] = "track.scrobble",
                ["artist"] = track.DisplayArtist,
                ["track"] = track.DisplayTitle,
                ["album"] = track.DisplayAlbum,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            }).ConfigureAwait(false);
        }
        catch
        {
            /* ignore */
        }
    }

    public static async Task<string> CreateSessionAsync(
        string apiKey,
        string apiSecret,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "auth.getMobileSession",
            ["username"] = username.Trim(),
            ["password"] = password,
            ["api_key"] = apiKey.Trim(),
        };
        parameters["api_sig"] = Sign(parameters, apiSecret.Trim());
        parameters["format"] = "json";

        using var content = new FormUrlEncodedContent(parameters);
        using var response = await Http.PostAsync("https://ws.audioscrobbler.com/2.0/", content, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("error", out _))
        {
            var message = doc.RootElement.TryGetProperty("message", out var msg)
                ? msg.GetString()
                : "Last.fm authentication failed";
            throw new InvalidOperationException(message);
        }

        if (doc.RootElement.TryGetProperty("session", out var session) &&
            session.TryGetProperty("key", out var key))
        {
            return key.GetString()
                   ?? throw new InvalidOperationException("Last.fm session key missing.");
        }

        throw new InvalidOperationException("Last.fm session key missing.");
    }

    private static async Task CallAsync(AppSettings settings, Dictionary<string, string> extra)
    {
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["api_key"] = settings.LastFmApiKey!,
            ["sk"] = settings.LastFmSessionKey!,
        };
        foreach (var (k, v) in extra)
            parameters[k] = v;

        parameters["api_sig"] = Sign(parameters, settings.LastFmApiSecret!);
        parameters["format"] = "json";

        using var content = new FormUrlEncodedContent(parameters);
        using var response = await Http.PostAsync("https://ws.audioscrobbler.com/2.0/", content).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || body.Contains("\"error\"", StringComparison.Ordinal))
            throw new InvalidOperationException(body);
    }

    private static string Sign(SortedDictionary<string, string> parameters, string apiSecret)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in parameters)
        {
            if (key is "format" or "callback")
                continue;
            sb.Append(key).Append(value);
        }

        sb.Append(apiSecret);
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
