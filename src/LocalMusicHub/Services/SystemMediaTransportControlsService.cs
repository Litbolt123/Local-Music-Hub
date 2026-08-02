using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Interop;
using LocalMusicHub.Models;
using Windows.Media;
using Windows.Storage.Streams;

namespace LocalMusicHub.Services;

/// <summary>
/// Publishes now-playing metadata and transport buttons to Windows System Media Transport
/// Controls (Quick Settings / lock screen media card).
/// </summary>
public sealed class SystemMediaTransportControlsService : IDisposable
{
    private readonly Window _window;
    private SystemMediaTransportControls? _smtc;
    private long? _displayedTrackId;
    private int _displayedCoverHash;
    private DateTime _lastTimelineUtc = DateTime.MinValue;
    private bool _disposed;

    public event Action? PlayRequested;
    public event Action? PauseRequested;
    public event Action? NextRequested;
    public event Action? PreviousRequested;
    public event Action? StopRequested;
    public event Action<TimeSpan>? SeekRequested;

    public SystemMediaTransportControlsService(Window window)
    {
        _window = window;
        _window.SourceInitialized += (_, _) => EnsureAttached();
        if (_window.IsLoaded || PresentationSource.FromVisual(_window) is not null)
            EnsureAttached();
    }

    public void EnsureAttached()
    {
        if (_disposed || _smtc is not null)
            return;

        try
        {
            var hwnd = new WindowInteropHelper(_window).EnsureHandle();
            if (hwnd == IntPtr.Zero)
                return;

            _smtc = SystemMediaTransportControlsInterop.GetForWindow(hwnd);
            _smtc.IsEnabled = false;
            _smtc.IsPlayEnabled = true;
            _smtc.IsPauseEnabled = true;
            _smtc.IsNextEnabled = true;
            _smtc.IsPreviousEnabled = true;
            _smtc.IsStopEnabled = true;
            _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
            _smtc.ButtonPressed += OnButtonPressed;
            _smtc.PlaybackPositionChangeRequested += OnPlaybackPositionChangeRequested;
        }
        catch
        {
            _smtc = null;
        }
    }

    public void Update(
        LibraryTrack? track,
        bool isPlaying,
        bool isPaused,
        TimeSpan position,
        TimeSpan duration)
    {
        if (_disposed)
            return;

        EnsureAttached();
        if (_smtc is null)
            return;

        try
        {
            if (track is null)
            {
                ClearDisplay();
                return;
            }

            _smtc.IsEnabled = true;
            _smtc.PlaybackStatus = isPlaying
                ? MediaPlaybackStatus.Playing
                : isPaused
                    ? MediaPlaybackStatus.Paused
                    : MediaPlaybackStatus.Stopped;

            var coverHash = track.CoverArt is { Length: > 0 }
                ? HashCode.Combine(track.CoverArt.Length, track.CoverArt[0], track.CoverArt[^1])
                : 0;

            if (_displayedTrackId != track.Id || _displayedCoverHash != coverHash)
            {
                _displayedTrackId = track.Id;
                _displayedCoverHash = coverHash;
                _ = ApplyMetadataAsync(track);
            }

            UpdateTimelineCore(position, duration, force: true);
        }
        catch
        {
            /* SMTC can fail on older builds / session edge cases */
        }
    }

    /// <summary>Throttled position sync for the system media timeline scrubber.</summary>
    public void UpdateTimeline(TimeSpan position, TimeSpan duration)
    {
        if (_disposed || _smtc is null || !_smtc.IsEnabled)
            return;

        if ((DateTime.UtcNow - _lastTimelineUtc).TotalMilliseconds < 900)
            return;

        try
        {
            UpdateTimelineCore(position, duration, force: false);
        }
        catch
        {
            /* ignore */
        }
    }

    private async Task ApplyMetadataAsync(LibraryTrack track)
    {
        if (_smtc is null)
            return;

        try
        {
            var updater = _smtc.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.AppMediaId = "Local Music Hub";
            updater.MusicProperties.Title = Truncate(track.DisplayTitle, 256);
            updater.MusicProperties.Artist = Truncate(track.DisplayArtist, 256);
            updater.MusicProperties.AlbumTitle = Truncate(track.DisplayAlbum, 256);
            if (!string.IsNullOrWhiteSpace(track.AlbumArtist))
                updater.MusicProperties.AlbumArtist = Truncate(track.AlbumArtist, 256);
            if (track.TrackNumber is int tn and > 0)
                updater.MusicProperties.TrackNumber = (uint)tn;

            updater.Thumbnail = await CreateThumbnailAsync(track.CoverArt).ConfigureAwait(false);
            // Skip stale updates if the track changed while we were encoding art.
            if (_displayedTrackId != track.Id)
                return;

            updater.Update();
        }
        catch
        {
            /* ignore */
        }
    }

    private void UpdateTimelineCore(TimeSpan position, TimeSpan duration, bool force)
    {
        if (_smtc is null)
            return;

        if (!force && (DateTime.UtcNow - _lastTimelineUtc).TotalMilliseconds < 900)
            return;

        var safeDuration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        var safePosition = position < TimeSpan.Zero
            ? TimeSpan.Zero
            : position > safeDuration && safeDuration > TimeSpan.Zero
                ? safeDuration
                : position;

        var timeline = new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero,
            MinSeekTime = TimeSpan.Zero,
            Position = safePosition,
            MaxSeekTime = safeDuration,
            EndTime = safeDuration,
        };
        _smtc.UpdateTimelineProperties(timeline);
        _lastTimelineUtc = DateTime.UtcNow;
    }

    private void ClearDisplay()
    {
        if (_smtc is null)
            return;

        _displayedTrackId = null;
        _displayedCoverHash = 0;
        _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
        _smtc.IsEnabled = false;
        var updater = _smtc.DisplayUpdater;
        updater.ClearAll();
        updater.Update();
        UpdateTimelineCore(TimeSpan.Zero, TimeSpan.Zero, force: true);
    }

    private static async Task<RandomAccessStreamReference?> CreateThumbnailAsync(byte[]? coverArt)
    {
        if (coverArt is not { Length: > 0 })
            return null;

        try
        {
            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(coverArt.AsBuffer());
            stream.Seek(0);
            return RandomAccessStreamReference.CreateFromStream(stream);
        }
        catch
        {
            return null;
        }
    }

    private void OnButtonPressed(
        SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
                PlayRequested?.Invoke();
                break;
            case SystemMediaTransportControlsButton.Pause:
                PauseRequested?.Invoke();
                break;
            case SystemMediaTransportControlsButton.Next:
                NextRequested?.Invoke();
                break;
            case SystemMediaTransportControlsButton.Previous:
                PreviousRequested?.Invoke();
                break;
            case SystemMediaTransportControlsButton.Stop:
                StopRequested?.Invoke();
                break;
        }
    }

    private void OnPlaybackPositionChangeRequested(
        SystemMediaTransportControls sender,
        PlaybackPositionChangeRequestedEventArgs args)
    {
        SeekRequested?.Invoke(args.RequestedPlaybackPosition);
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? "" : value.Length <= max ? value : value[..(max - 1)] + "…";

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            if (_smtc is not null)
            {
                _smtc.ButtonPressed -= OnButtonPressed;
                _smtc.PlaybackPositionChangeRequested -= OnPlaybackPositionChangeRequested;
                ClearDisplay();
            }
        }
        catch
        {
            /* ignore */
        }

        _smtc = null;
    }
}
