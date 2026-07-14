using DiscordRPC;
using LocalMusicHub.Models;

namespace LocalMusicHub.Services;

public sealed class DiscordPresenceService : IDisposable
{
    private DiscordRpcClient? _client;
    private bool _enabled;
    private string? _clientId;
    private string? _connectedClientId;
    private DateTime? _trackStartedUtc;

    public void ApplySettings(AppSettings settings)
    {
        _enabled = settings.DiscordRichPresenceEnabled;
        _clientId = string.IsNullOrWhiteSpace(settings.DiscordClientId)
            ? null
            : settings.DiscordClientId.Trim();

        if (!_enabled || _clientId is null)
        {
            Clear();
            DisposeClient();
            return;
        }

        EnsureClient();
    }

    public void Update(LibraryTrack? track, bool isPlaying)
    {
        if (!_enabled || _clientId is null)
        {
            Clear();
            return;
        }

        EnsureClient();
        if (_client is null || !_client.IsInitialized)
            return;

        if (track is null || !isPlaying)
        {
            Clear();
            return;
        }

        _trackStartedUtc ??= DateTime.UtcNow;
        try
        {
            _client.SetPresence(new RichPresence
            {
                Details = Truncate(track.DisplayTitle, 128),
                State = Truncate($"by {track.DisplayArtist}", 128),
                Assets = new Assets
                {
                    LargeImageKey = "music",
                    LargeImageText = Truncate(track.DisplayAlbum, 128),
                },
                Timestamps = new Timestamps(_trackStartedUtc.Value),
            });
        }
        catch
        {
            /* Discord not running / IPC unavailable */
        }
    }

    public void OnTrackChanged()
    {
        _trackStartedUtc = DateTime.UtcNow;
    }

    public void Clear()
    {
        try
        {
            _client?.ClearPresence();
        }
        catch
        {
            /* ignore */
        }
    }

    private void EnsureClient()
    {
        if (_clientId is null)
            return;

        if (_client is not null &&
            string.Equals(_connectedClientId, _clientId, StringComparison.Ordinal))
            return;

        DisposeClient();
        try
        {
            _client = new DiscordRpcClient(_clientId);
            _client.Initialize();
            _connectedClientId = _clientId;
        }
        catch
        {
            DisposeClient();
        }
    }

    private void DisposeClient()
    {
        try
        {
            _client?.Dispose();
        }
        catch
        {
            /* ignore */
        }

        _client = null;
        _connectedClientId = null;
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? "" : value.Length <= max ? value : value[..(max - 1)] + "…";

    public void Dispose()
    {
        Clear();
        DisposeClient();
    }
}
