using LocalMusicHub.Data;
using LocalMusicHub.Services;

namespace LocalMusicHub;

public partial class MainWindow
{
    private LibraryDataServices? _dataServices;
    private PlaybackService? _playbackService;
    private YouTubeDownloaderBridge? _downloaderBridgeService;
    private LibraryIngestHost? _libraryIngestHost;
    private YouTubeDownloaderApiClient? _downloaderApiService;
    private TrayIconService? _trayService;
    private DiscordPresenceService? _discordService;
    private LastFmScrobbler? _lastFmService;
    private AcoustIdService? _acoustIdService;
    private ScriptHookService? _scriptHooksService;
    private LyricsPrefetchService? _lyricsPrefetchService;

    private bool _playbackEventsWired;
    private bool _trayEventsWired;
    private bool _downloaderEventsWired;
    private bool _ingestEventsWired;
    private bool _lyricsPrefetchEventsWired;
    private bool _libraryUiReady;
    private bool _folderWatcherHooked;
    private readonly object _lyricsPrefetchGate = new();

    private LibraryDataServices DataServices => _dataServices ??= new();
    private LibraryRepository Repository => DataServices.Repository;
    private LibraryScanner Scanner => DataServices.Scanner;
    private LibraryFolderWatcher FolderWatcher => DataServices.FolderWatcher;

    private PlaybackService Playback => EnsurePlayback();

    private PlaybackService EnsurePlayback()
    {
        if (_playbackService is not null)
            return _playbackService;

        StartupProfiler.Mark("lazy.playback.create");
        _playbackService = new PlaybackService();
        WirePlaybackEvents();
        return _playbackService;
    }

    private YouTubeDownloaderBridge DownloaderBridge
    {
        get
        {
            if (_downloaderBridgeService is not null)
                return _downloaderBridgeService;

            StartupProfiler.Mark("lazy.downloader_bridge.create");
            _downloaderBridgeService = new YouTubeDownloaderBridge();
            WireDownloaderEvents();
            return _downloaderBridgeService;
        }
    }

    private LibraryIngestHost LibraryIngest
    {
        get
        {
            if (_libraryIngestHost is not null)
                return _libraryIngestHost;

            StartupProfiler.Mark("lazy.library_ingest.create");
            _libraryIngestHost = new LibraryIngestHost();
            WireIngestEvents();
            return _libraryIngestHost;
        }
    }

    private void ApplyLibraryIngestHost()
    {
        var hadToken = !string.IsNullOrWhiteSpace(App.Settings.LibraryIngestToken);
        AppSettingsService.EnsureLibraryIngestToken(App.Settings);
        if (!hadToken)
            App.SaveSettings();
        LibraryIngest.ApplySettings(App.Settings);
    }

    private YouTubeDownloaderApiClient DownloaderApi =>
        _downloaderApiService ??= new YouTubeDownloaderApiClient();

    private TrayIconService Tray
    {
        get
        {
            if (_trayService is not null)
                return _trayService;

            StartupProfiler.Mark("lazy.tray.create");
            _trayService = new TrayIconService();
            _trayService.Attach(this);
            WireTrayEvents();
            return _trayService;
        }
    }

    private DiscordPresenceService Discord =>
        _discordService ??= new DiscordPresenceService();

    private LastFmScrobbler LastFm =>
        _lastFmService ??= new LastFmScrobbler();

    private AcoustIdService AcoustId =>
        _acoustIdService ??= new AcoustIdService();

    private ScriptHookService ScriptHooks =>
        _scriptHooksService ??= new ScriptHookService();

    private LyricsPrefetchService EnsureLyricsPrefetch(bool enabled = true)
    {
        lock (_lyricsPrefetchGate)
        {
            if (_lyricsPrefetchService is null)
            {
                StartupProfiler.Mark("lazy.lyrics_prefetch.create");
                _lyricsPrefetchService = new LyricsPrefetchService();
                Dispatcher.BeginInvoke(WireLyricsPrefetchEvents);
            }

            _lyricsPrefetchService.Enabled = enabled;
            return _lyricsPrefetchService;
        }
    }

    private void EnsureLyricsPrefetchOnBackground(bool enabled)
    {
        if (_lyricsPrefetchService is not null)
        {
            _lyricsPrefetchService.Enabled = enabled;
            return;
        }

        _ = Task.Run(() => EnsureLyricsPrefetch(enabled));
    }

    private void EnsurePlaybackConfigured()
    {
        StartupProfiler.Mark("lazy.playback.configure");
        var playback = EnsurePlayback();
        playback.SetVolume(App.PendingVolume ?? App.Settings.DefaultVolume);
        if (App.PendingVolume is not null)
        {
            App.Settings.DefaultVolume = App.PendingVolume.Value;
            App.ClearPendingVolume();
        }
        ProcessPendingVolumeRequest();
        playback.Configure(App.Settings.Shuffle, PlaybackRepeatModeExtensions.Parse(App.Settings.RepeatMode));
        playback.ShouldStopInsteadOfAdvancing = () =>
        {
            if (_sleepTimer is null || !_sleepTimer.IsActive)
                return false;
            var atLast = playback.QueueIndex >= playback.Queue.Count - 1;
            return _sleepTimer.ShouldStopInsteadOfAdvancing(atLast);
        };
        RefreshQueueList();
        UpdateShuffleRepeatChrome();
        RefreshNowPlaying();
    }

    private void WirePlaybackEvents()
    {
        if (_playbackEventsWired || _playbackService is null)
            return;

        _playbackEventsWired = true;
        _playbackService.TrackChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            Discord.OnTrackChanged();
            ScriptHooks.OnTrackChanged(Playback.CurrentTrack);
            RefreshNowPlaying();
            RefreshQueueList();
            UpdateDiscordPresence();
            NotifyTrayTrackChanged();
            if (_lyricsWindow is { IsLoaded: true } && Playback.CurrentTrack is { } current)
                _lyricsWindow.ShowTrack(current);
            _miniPlayer?.Refresh();
        });
        _playbackService.StateChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            RefreshNowPlaying();
            UpdateDiscordPresence();
            _miniPlayer?.Refresh();
        });
        _playbackService.QueueChanged += (_, _) => Dispatcher.BeginInvoke(RefreshQueueList);
        _playbackService.TrackStarted += Playback_OnTrackStarted;
    }

    private void WireTrayEvents()
    {
        if (_trayEventsWired || _trayService is null)
            return;

        _trayEventsWired = true;
        _trayService.PlayPauseRequested += () => Dispatcher.Invoke(() =>
        {
            if (Playback.CurrentTrack is null)
                return;
            Playback.TogglePlayPause();
            RefreshNowPlaying();
        });
        _trayService.NextRequested += () => Dispatcher.Invoke(() => Playback.Next());
        _trayService.PreviousRequested += () => Dispatcher.Invoke(() => Playback.Previous());
    }

    private void WireDownloaderEvents()
    {
        if (_downloaderEventsWired || _downloaderBridgeService is null)
            return;

        _downloaderEventsWired = true;
        _downloaderBridgeService.NewAudioFileDetected += DownloaderBridge_OnNewAudioFileDetected;
    }

    private void WireIngestEvents()
    {
        if (_ingestEventsWired || _libraryIngestHost is null)
            return;

        _ingestEventsWired = true;
        _libraryIngestHost.IngestRequested += LibraryIngest_OnRequested;
    }

    private void LibraryIngest_OnRequested(object? sender, LibraryIngestEvent e)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            if (e.ImportFolder)
                ImportAudioFolder(e.Path, fromExternalRequest: false);
            else
                await ImportDownloaderFileAsync(e.Path).ConfigureAwait(true);

            if (_pendingDownloadUrl is not null && !e.ImportFolder)
                ClearPendingDownloadWatch();
        });
    }

    private void WireLyricsPrefetchEvents()
    {
        if (_lyricsPrefetchEventsWired || _lyricsPrefetchService is null)
            return;

        _lyricsPrefetchEventsWired = true;
        _lyricsPrefetchService.ProgressChanged += (_, p) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (p.Total <= 0)
                    return;

                if (!ViewTitleText.Text.StartsWith("Downloading lyrics", StringComparison.Ordinal))
                    return;

                if (p.IsIdle)
                {
                    var skipped = p.SkippedNotFound > 0 ? $", skipped {p.SkippedNotFound}" : "";
                    ViewTitleText.Text =
                        $"Lyrics download done — saved {p.Saved}, already had {p.AlreadyHad}, not found {p.NotFound}{skipped}";
                }
                else
                {
                    ViewTitleText.Text = $"Downloading lyrics {p.Done}/{p.Total}" +
                        (string.IsNullOrWhiteSpace(p.CurrentTitle) ? "…" : $" — {p.CurrentTitle}");
                }
            });
        };
    }

    private void DisposeLazyServices()
    {
        _sleepTimer?.Dispose();
        _mediaKeys?.Dispose();
        _lyricsPrefetchService?.Dispose();
        _discordService?.Dispose();
        _playbackService?.Dispose();
        _dataServices?.DisposeAll();
        _downloaderBridgeService?.Dispose();
        _libraryIngestHost?.Dispose();
        _downloaderApiService?.Dispose();
        _trayService?.Dispose();
    }
}
