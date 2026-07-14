using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using LocalMusicHub.Data;
using LocalMusicHub.Models;
using LocalMusicHub.Services;
using MessageBox = System.Windows.MessageBox;

namespace LocalMusicHub;

public partial class MainWindow
{
    private readonly LibraryDatabase _database = new(AppPaths.DatabasePath);
    private readonly LibraryRepository _repository;
    private readonly LibraryScanner _scanner;
    private readonly LibraryFolderWatcher _folderWatcher;
    private readonly PlaybackService _playback = new();
    private readonly YouTubeDownloaderBridge _downloaderBridge = new();
    private readonly YouTubeDownloaderApiClient _downloaderApi = new();
    private readonly TrayIconService _tray = new();
    private readonly DiscordPresenceService _discord = new();
    private readonly LastFmScrobbler _lastFm = new();
    private readonly AcoustIdService _acoustId = new();
    private readonly ScriptHookService _scriptHooks = new();
    private readonly LyricsPrefetchService _lyricsPrefetch = new();
    private SleepTimerService? _sleepTimer;
    private GlobalMediaKeyService? _mediaKeys;
    private MiniPlayerWindow? _miniPlayer;
    private readonly DispatcherTimer _positionTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _searchDebounceTimer;
    private double _volumeBeforeMute = 0.85;
    private bool _muted;
    private bool _suppressVolumeSlider;
    private bool _suppressSpeedCombo;

    private string _browseMode = "home";
    private long? _selectedPlaylistId;
    private string? _albumDrillDown;
    private string? _albumDrillDownMemory;
    private string? _listDrillDown; // artist or genre name when drilled in
    private string? _listDrillDownKind; // "artists" or "genres"
    private readonly HashSet<string> _quickFilters = new(StringComparer.OrdinalIgnoreCase);
    private LibraryAlbum? _selectedAlbum;
    private bool _seeking;
    private bool _forceClose;
    private bool _startMinimizedToTray;
    private bool _scanInProgress;
    private bool _suppressNavEvents;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _downloadPollCts;
    private FileSystemWatcher? _importRequestWatcher;
    private string? _downloaderJobStatus;
    private string? _pendingDownloadUrl;
    private DateTime? _pendingDownloadQueuedUtc;
    private UpdateCheckResult? _pendingUpdate;

    public MainWindow()
    {
        HubTheme.Ensure(this);
        InitializeComponent();
        _repository = new LibraryRepository(_database);
        _scanner = new LibraryScanner(_repository);
        _folderWatcher = new LibraryFolderWatcher(_repository);
        _tray.Attach(this);
        _startMinimizedToTray = AutoStartService.ArgsRequestTray(Environment.GetCommandLineArgs());
        if (App.Settings.MinimizeToTray || App.Settings.StartWithWindows || _startMinimizedToTray)
            _tray.ShowTrayIcon();

        _playback.TrackChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            _discord.OnTrackChanged();
            _scriptHooks.OnTrackChanged(_playback.CurrentTrack);
            RefreshNowPlaying();
            RefreshQueueList();
            UpdateDiscordPresence();
            NotifyTrayTrackChanged();
            if (_lyricsWindow is { IsLoaded: true } && _playback.CurrentTrack is { } current)
                _lyricsWindow.ShowTrack(current);
            _miniPlayer?.Refresh();
        });
        _playback.StateChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            RefreshNowPlaying();
            UpdateDiscordPresence();
            _miniPlayer?.Refresh();
        });
        _playback.QueueChanged += (_, _) => Dispatcher.Invoke(RefreshQueueList);
        _playback.TrackStarted += Playback_OnTrackStarted;
        _folderWatcher.LibraryChanged += (_, _) => Dispatcher.Invoke(RefreshLibraryViews);
        _downloaderBridge.NewAudioFileDetected += DownloaderBridge_OnNewAudioFileDetected;
        _positionTimer.Tick += PositionTimer_OnTick;
        _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _searchDebounceTimer.Tick += SearchDebounceTimer_OnTick;
        _tray.PlayPauseRequested += () => Dispatcher.Invoke(() =>
        {
            if (_playback.CurrentTrack is null)
                return;
            _playback.TogglePlayPause();
            RefreshNowPlaying();
        });
        _tray.NextRequested += () => Dispatcher.Invoke(() => _playback.Next());
        _tray.PreviousRequested += () => Dispatcher.Invoke(() => _playback.Previous());
        _lyricsPrefetch.ProgressChanged += (_, p) =>
        {
            Dispatcher.Invoke(() =>
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
        SetupImportRequestWatcher();
    }

    private void SetupImportRequestWatcher()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            _importRequestWatcher = new FileSystemWatcher(AppPaths.DataDirectory)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };
            _importRequestWatcher.Created += OnInstanceSignalFileChanged;
            _importRequestWatcher.Changed += OnInstanceSignalFileChanged;
        }
        catch
        {
            /* ignore */
        }
    }

    private void OnInstanceSignalFileChanged(object sender, FileSystemEventArgs e)
    {
        var name = Path.GetFileName(e.FullPath);
        if (string.Equals(name, "import-request.json", StringComparison.OrdinalIgnoreCase))
            Dispatcher.Invoke(ProcessPendingImportRequest);
        else if (string.Equals(name, "activate.signal", StringComparison.OrdinalIgnoreCase))
            Dispatcher.Invoke(ActivateFromSecondInstance);
    }

    private void ActivateFromSecondInstance()
    {
        try
        {
            if (File.Exists(SingleInstanceService.ActivateSignalPath))
                File.Delete(SingleInstanceService.ActivateSignalPath);
        }
        catch
        {
            /* ignore */
        }

        _tray.ShowMainWindow();
    }

    private void ProcessPendingImportRequest()
    {
        if (!LibraryImportRequestService.TryReadPending(out var request) || request is null)
            return;

        LibraryImportRequestService.ClearPending();
        if (request.ImportFolder)
            ImportAudioFolder(request.Path, fromExternalRequest: true);
        else
            ImportAudioFile(request.Path, fromExternalRequest: true);
    }

    private void ProcessStartupImport()
    {
        if (!string.IsNullOrWhiteSpace(App.PendingImportPath))
        {
            if (App.PendingImportFolder)
                ImportAudioFolder(App.PendingImportPath, fromExternalRequest: true);
            else
                ImportAudioFile(App.PendingImportPath, fromExternalRequest: true);
            return;
        }

        ProcessPendingImportRequest();
    }

    private void MainWindow_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            if (SearchBox is not null && SearchBox.Visibility == Visibility.Visible)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Space && !IsTextInputFocused())
        {
            if (_playback.CurrentTrack is not null)
            {
                _playback.TogglePlayPause();
                RefreshNowPlaying();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.MediaPlayPause)
        {
            if (_playback.CurrentTrack is not null)
            {
                _playback.TogglePlayPause();
                RefreshNowPlaying();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.MediaNextTrack)
        {
            _playback.Next();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.MediaPreviousTrack)
        {
            _playback.Previous();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
            return;
        if (_browseMode != "albums" || _selectedAlbum is null)
            return;
        if (SearchBox?.IsKeyboardFocusWithin == true)
            return;

        OpenAlbumTracks(_selectedAlbum);
        e.Handled = true;
    }

    private static bool IsTextInputFocused()
    {
        var focused = Keyboard.FocusedElement;
        return focused is System.Windows.Controls.TextBox
            or System.Windows.Controls.Primitives.TextBoxBase
            or System.Windows.Controls.PasswordBox;
    }

    private void SearchDebounceTimer_OnTick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        if (!IsLoaded)
            return;
        if (_browseMode is "albums" or "queue" or "home")
            return;
        RefreshLibraryViews();
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        _playback.SetVolume(App.Settings.DefaultVolume);
        _suppressVolumeSlider = true;
        if (VolumeSlider is not null)
            VolumeSlider.Value = App.Settings.DefaultVolume;
        _suppressVolumeSlider = false;
        SyncSpeedComboFromSettings();
        ApplySidebarLayoutFromSettings();
        UpdateMuteChrome();
        _playback.Configure(App.Settings.Shuffle, PlaybackRepeatModeExtensions.Parse(App.Settings.RepeatMode));
        UpdateShuffleRepeatChrome();
        _downloaderBridge.Apply(App.Settings);
        _discord.ApplySettings(App.Settings);
        _scriptHooks.Enabled = App.Settings.ScriptHooksEnabled;
        if (_scriptHooks.Enabled)
            _scriptHooks.EnsureScriptsFolder();
        _lyricsPrefetch.Enabled = App.Settings.AutoDownloadLyrics;
        _sleepTimer = new SleepTimerService(
            vol =>
            {
                _suppressVolumeSlider = true;
                _playback.SetVolume(vol);
                if (VolumeSlider is not null)
                    VolumeSlider.Value = vol;
                _suppressVolumeSlider = false;
                UpdateMuteChrome();
            },
            () =>
            {
                _playback.Pause();
                RefreshNowPlaying();
            });
        _sleepTimer.Changed += (_, _) => Dispatcher.Invoke(UpdateSleepTimerChrome);
        _mediaKeys = new GlobalMediaKeyService(this);
        _mediaKeys.PlayPauseRequested += () => Dispatcher.Invoke(() =>
        {
            if (_playback.CurrentTrack is null)
                return;
            _playback.TogglePlayPause();
            RefreshNowPlaying();
        });
        _mediaKeys.NextRequested += () => Dispatcher.Invoke(() => _playback.Next());
        _mediaKeys.PreviousRequested += () => Dispatcher.Invoke(() => _playback.Previous());
        _mediaKeys.StopRequested += () => Dispatcher.Invoke(() =>
        {
            _playback.Pause();
            RefreshNowPlaying();
        });
        _mediaKeys.MuteRequested += () => Dispatcher.Invoke(() => Mute_OnClick(this, new RoutedEventArgs()));
        _mediaKeys.VolumeUpRequested += () => Dispatcher.Invoke(() => AdjustVolume(+0.05));
        _mediaKeys.VolumeDownRequested += () => Dispatcher.Invoke(() => AdjustVolume(-0.05));
        _playback.ShouldStopInsteadOfAdvancing = () =>
        {
            if (_sleepTimer is null || !_sleepTimer.IsActive)
                return false;
            var atLast = _playback.QueueIndex >= _playback.Queue.Count - 1;
            return _sleepTimer.ShouldStopInsteadOfAdvancing(atLast);
        };
        App.SaveSettings();
        ApplyFolderWatcher();
        ApplyDownloaderSidebarLayout();
        RefreshDownloaderStatus();
        ProcessStartupImport();
        RefreshPlaylistNav();
        RefreshLibraryViews();
        RefreshQueueList();
        UpdateOpenButtonVisibility();
        _positionTimer.Start();

        if (_startMinimizedToTray || (App.Settings.StartWithWindows && AutoStartService.ArgsRequestTray(Environment.GetCommandLineArgs())))
        {
            _tray.ShowTrayIcon();
            Hide();
            return;
        }

        if (_repository.TrackCount() == 0)
            _ = ScanLibraryAsync();
    }

    public void RequestForceClose() => _forceClose = true;

    public void OnTrayPreferenceChanged()
    {
        if (App.Settings.MinimizeToTray || App.Settings.StartWithWindows)
            _tray.ShowTrayIcon();
        else if (!IsVisible)
            _tray.ShowMainWindow();
        else
            _tray.HideTrayIcon();
    }

    private void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_forceClose && (App.Settings.MinimizeToTray || App.Settings.StartWithWindows))
        {
            e.Cancel = true;
            _tray.MinimizeToTray();
            return;
        }

        _scanCts?.Cancel();
        _positionTimer.Stop();
        _miniPlayer?.CloseFromOwner();
        _sleepTimer?.Dispose();
        _mediaKeys?.Dispose();
        _lyricsPrefetch.Dispose();
        _discord.Dispose();
        _playback.Dispose();
        _folderWatcher.Dispose();
        _downloaderBridge.Dispose();
        _downloaderApi.Dispose();
        _downloadPollCts?.Cancel();
        _downloadPollCts?.Dispose();
        _importRequestWatcher?.Dispose();
        _tray.Dispose();
        _database.Dispose();
        PersistSidebarLayout();
        App.SaveSettings();
    }

    private void PersistSidebarLayout()
    {
        if (LeftPanelColumn is null || RightPanelColumn is null)
            return;

        if (App.Settings.LeftSidebarVisible && LeftPanelColumn.Width.IsAbsolute && LeftPanelColumn.Width.Value >= 160)
            App.Settings.LeftSidebarWidth = LeftPanelColumn.Width.Value;
        if (App.Settings.RightSidebarVisible && RightPanelColumn.Width.IsAbsolute && RightPanelColumn.Width.Value >= 200)
            App.Settings.RightSidebarWidth = RightPanelColumn.Width.Value;
    }

    private void ApplySidebarLayoutFromSettings()
    {
        if (LeftPanelColumn is null || RightPanelColumn is null)
            return;

        var leftW = Math.Clamp(App.Settings.LeftSidebarWidth, 180, 480);
        var rightW = Math.Clamp(App.Settings.RightSidebarWidth, 220, 520);
        App.Settings.LeftSidebarWidth = leftW;
        App.Settings.RightSidebarWidth = rightW;

        SetLeftSidebarVisible(App.Settings.LeftSidebarVisible, persist: false);
        SetRightSidebarVisible(App.Settings.RightSidebarVisible, persist: false);
        if (App.Settings.LeftSidebarVisible)
            LeftPanelColumn.Width = new GridLength(leftW);
        if (App.Settings.RightSidebarVisible)
            RightPanelColumn.Width = new GridLength(rightW);
    }

    private void SetLeftSidebarVisible(bool visible, bool persist = true)
    {
        App.Settings.LeftSidebarVisible = visible;
        if (visible)
        {
            var w = Math.Clamp(App.Settings.LeftSidebarWidth, 180, 480);
            LeftPanelColumn.Width = new GridLength(w);
            LeftPanelColumn.MinWidth = 180;
            LeftSplitterColumn.Width = new GridLength(6);
            LeftSidebarBorder.Visibility = Visibility.Visible;
            LeftPanelSplitter.Visibility = Visibility.Visible;
            ShowLeftSidebarButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            if (LeftPanelColumn.Width.IsAbsolute && LeftPanelColumn.Width.Value >= 160)
                App.Settings.LeftSidebarWidth = LeftPanelColumn.Width.Value;
            LeftPanelColumn.MinWidth = 0;
            LeftPanelColumn.Width = new GridLength(0);
            LeftSplitterColumn.Width = new GridLength(0);
            LeftSidebarBorder.Visibility = Visibility.Collapsed;
            LeftPanelSplitter.Visibility = Visibility.Collapsed;
            ShowLeftSidebarButton.Visibility = Visibility.Visible;
        }

        if (persist)
            App.SaveSettings();
    }

    private void SetRightSidebarVisible(bool visible, bool persist = true)
    {
        App.Settings.RightSidebarVisible = visible;
        if (visible)
        {
            var w = Math.Clamp(App.Settings.RightSidebarWidth, 220, 520);
            RightPanelColumn.Width = new GridLength(w);
            RightPanelColumn.MinWidth = 220;
            RightSplitterColumn.Width = new GridLength(6);
            RightSidebarBorder.Visibility = Visibility.Visible;
            RightPanelSplitter.Visibility = Visibility.Visible;
            ShowRightSidebarButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            if (RightPanelColumn.Width.IsAbsolute && RightPanelColumn.Width.Value >= 200)
                App.Settings.RightSidebarWidth = RightPanelColumn.Width.Value;
            RightPanelColumn.MinWidth = 0;
            RightPanelColumn.Width = new GridLength(0);
            RightSplitterColumn.Width = new GridLength(0);
            RightSidebarBorder.Visibility = Visibility.Collapsed;
            RightPanelSplitter.Visibility = Visibility.Collapsed;
            ShowRightSidebarButton.Visibility = Visibility.Visible;
        }

        if (persist)
            App.SaveSettings();
    }

    private void HideLeftSidebar_OnClick(object sender, RoutedEventArgs e) => SetLeftSidebarVisible(false);
    private void ShowLeftSidebar_OnClick(object sender, RoutedEventArgs e) => SetLeftSidebarVisible(true);
    private void HideRightSidebar_OnClick(object sender, RoutedEventArgs e) => SetRightSidebarVisible(false);
    private void ShowRightSidebar_OnClick(object sender, RoutedEventArgs e) => SetRightSidebarVisible(true);

    private void PanelSplitter_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        PersistSidebarLayout();
        App.SaveSettings();
    }

    private void LibraryTools_OnClick(object sender, RoutedEventArgs e)
    {
        var trackCount = _repository.GetAllTracks().Count;
        var dlg = new LibraryToolsWindow(
            trackCount,
            onStats: () => LibraryStats_OnClick(this, new RoutedEventArgs()),
            onDuplicates: () => FindDuplicates_OnClick(this, new RoutedEventArgs()),
            onOrganize: () => OrganizeFiles_OnClick(this, new RoutedEventArgs()),
            onReplayGain: () => ScanReplayGain_OnClick(this, new RoutedEventArgs()),
            onCleanDead: () => CleanDead_OnClick(this, new RoutedEventArgs()),
            onScanLibrary: () => ScanLibrary_OnClick(this, new RoutedEventArgs()))
        {
            Owner = this,
        };
        dlg.ShowDialog();
    }

    private void ApplyFolderWatcher()
    {
        var roots = App.Settings.LibraryFolders.Where(Directory.Exists).Distinct().ToList();
        if (roots.Count == 0)
            roots.Add(AppPaths.DefaultMusicFolder);
        _folderWatcher.Apply(roots, App.Settings.WatchLibraryFolders);
    }

    private void ApplyDownloaderSidebarLayout()
    {
        if (DownloaderSidebarCard is null)
            return;

        if (!App.Settings.ShowYouTubeDownloaderSidebar)
        {
            DownloaderSidebarCard.Visibility = Visibility.Collapsed;
            return;
        }

        DownloaderSidebarCard.Visibility = Visibility.Visible;
        var collapsed = App.Settings.YouTubeDownloaderSidebarCollapsed;
        if (DownloaderSidebarContent is not null)
            DownloaderSidebarContent.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;

        if (DownloaderCollapseButton is not null)
        {
            DownloaderCollapseButton.Content = collapsed ? "▸" : "▾";
            DownloaderCollapseButton.ToolTip = collapsed ? "Expand panel" : "Collapse panel";
        }
    }

    private void DownloaderCollapse_OnClick(object sender, RoutedEventArgs e)
    {
        App.Settings.YouTubeDownloaderSidebarCollapsed = !App.Settings.YouTubeDownloaderSidebarCollapsed;
        App.SaveSettings();
        ApplyDownloaderSidebarLayout();
    }

    private void RefreshDownloaderStatus()
    {
        if (DownloaderStatusText is null)
            return;

        UpdateDownloaderControlsEnabled();

        if (!string.IsNullOrWhiteSpace(_downloaderJobStatus))
        {
            DownloaderStatusText.Text = _downloaderJobStatus;
            return;
        }

        if (!App.Settings.IntegrateYouTubeDownloader)
        {
            DownloaderStatusText.Text = "Integration disabled — enable in Settings to download from here.";
            return;
        }

        var status = _downloaderBridge.GetStatus();
        if (!status.Installed)
        {
            DownloaderStatusText.Text = "YouTube Downloader not detected.";
            return;
        }

        if (!status.SettingsFound)
        {
            DownloaderStatusText.Text = "Downloader installed; run it once to create settings.";
            return;
        }

        if (!status.ExtensionEnabled)
        {
            DownloaderStatusText.Text = "Linked — enable the browser extension API in YouTube Downloader to queue downloads.";
            return;
        }

        DownloaderStatusText.Text = status.MusicFolder is { Length: > 0 } folder
            ? $"Linked — API ready on port {status.ExtensionPort}.\nWatching: {folder}"
            : $"Linked — API ready on port {status.ExtensionPort}.";
    }

    private void UpdateDownloaderControlsEnabled()
    {
        if (DownloaderUrlBox is null || DownloaderDownloadButton is null || DownloaderPasteButton is null)
            return;

        var enabled = App.Settings.IntegrateYouTubeDownloader;
        DownloaderUrlBox.IsEnabled = enabled;
        DownloaderDownloadButton.IsEnabled = enabled;
        DownloaderPasteButton.IsEnabled = enabled;
    }

    private void SetDownloaderJobStatus(string? status)
    {
        _downloaderJobStatus = status;
        RefreshDownloaderStatus();
    }

    private async void DownloaderDownload_OnClick(object sender, RoutedEventArgs e) =>
        await QueueDownloaderUrlAsync(DownloaderUrlBox?.Text?.Trim());

    private async void DownloaderPasteDownload_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsText())
            {
                MessageBox.Show(this, "Clipboard is empty.", "YouTube Downloader", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var text = System.Windows.Clipboard.GetText()?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (DownloaderUrlBox is not null)
                DownloaderUrlBox.Text = text;

            await QueueDownloaderUrlAsync(text);
        }
        catch (Exception ex)
        {
            SetDownloaderJobStatus($"Error: {ex.Message}");
        }
    }

    private async Task QueueDownloaderUrlAsync(string? url)
    {
        if (!App.Settings.IntegrateYouTubeDownloader)
        {
            MessageBox.Show(this, "Enable YouTube Downloader integration in Settings first.", "YouTube Downloader",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!YouTubeUrlHelper.IsYouTubeUrl(url))
        {
            MessageBox.Show(this, "Enter a valid YouTube URL.", "YouTube Downloader", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _downloaderBridge.Apply(App.Settings);
        var link = _downloaderBridge.GetStatus();
        if (!link.ExtensionEnabled)
        {
            SetDownloaderJobStatus("Enable the browser extension API in YouTube Downloader.");
            return;
        }

        var token = App.Settings.YouTubeDownloaderToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            SetDownloaderJobStatus("Extension token missing — run YouTube Downloader once.");
            return;
        }

        var port = App.Settings.YouTubeDownloaderPort;
        SetDownloaderJobStatus("Connecting to YouTube Downloader…");
        DownloaderDownloadButton.IsEnabled = false;
        DownloaderPasteButton.IsEnabled = false;

        try
        {
            var health = await _downloaderApi.HealthAsync(port).ConfigureAwait(true);
            if (!health.Ok)
            {
                SetDownloaderJobStatus(health.Error ?? "YouTube Downloader is offline.");
                return;
            }

            var check = await _downloaderApi.CheckAsync(port, token, url!).ConfigureAwait(true);
            if (!check.Ok)
            {
                SetDownloaderJobStatus(check.Error ?? "Could not check URL.");
                return;
            }

            var forceRedownload = false;
            if (check.AlreadyDownloaded)
            {
                var title = string.IsNullOrWhiteSpace(check.Title) ? "this track" : check.Title;
                var answer = MessageBox.Show(this,
                    $"\"{title}\" was downloaded before.\n\nDownload again?",
                    "Already downloaded",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                {
                    ClearDownloaderJobStatus();
                    return;
                }

                forceRedownload = true;
            }
            else if (check.InQueue)
            {
                SetDownloaderJobStatus("Already in the YouTube Downloader queue — waiting for file…");
                BeginDownloadCompletionWatch(url!);
                return;
            }

            var download = await _downloaderApi.DownloadAsync(port, token, url!, forceRedownload).ConfigureAwait(true);
            if (!download.Ok)
            {
                SetDownloaderJobStatus(download.Error ?? "Download was not queued.");
                return;
            }

            SetDownloaderJobStatus("Queued — downloading via YouTube Downloader…");
            BeginDownloadCompletionWatch(url!);
        }
        finally
        {
            UpdateDownloaderControlsEnabled();
        }
    }

    private void BeginDownloadCompletionWatch(string url)
    {
        _downloadPollCts?.Cancel();
        _downloadPollCts?.Dispose();
        _downloadPollCts = new CancellationTokenSource();
        _pendingDownloadUrl = url;
        _pendingDownloadQueuedUtc = DateTime.UtcNow;
        _ = PollForDownloadCompletionAsync(_downloadPollCts.Token);
    }

    private async Task PollForDownloadCompletionAsync(CancellationToken cancellationToken)
    {
        var url = _pendingDownloadUrl;
        var queuedAt = _pendingDownloadQueuedUtc ?? DateTime.UtcNow;
        var deadline = DateTime.UtcNow.AddMinutes(15);

        try
        {
            while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(true);

                if (!string.Equals(_pendingDownloadUrl, url, StringComparison.Ordinal))
                    return;

                var entry = DownloaderHistoryReader.FindSuccessfulByUrl(url!, queuedAt);
                if (entry is not null)
                {
                    await Dispatcher.InvokeAsync(() => CompletePendingDownloadFromHistory(entry));
                    return;
                }

                if (_downloaderJobStatus?.StartsWith("Waiting", StringComparison.Ordinal) != true)
                    SetDownloaderJobStatus("Waiting for file from YouTube Downloader…");
            }

            if (!cancellationToken.IsCancellationRequested)
                SetDownloaderJobStatus("Timed out waiting for download. Check YouTube Downloader.");
        }
        catch (OperationCanceledException)
        {
            /* replaced by a newer watch */
        }
    }

    private void CompletePendingDownloadFromHistory(DownloaderHistoryEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.OutputPath) && File.Exists(entry.OutputPath))
            ImportDownloaderFile(entry.OutputPath, entry.Title);
        else
            SetDownloaderJobStatus($"Downloaded: {entry.Title} (file not found yet — watching folder)");

        ClearPendingDownloadWatch();
    }

    private void ClearPendingDownloadWatch()
    {
        _downloadPollCts?.Cancel();
        _downloadPollCts?.Dispose();
        _downloadPollCts = null;
        _pendingDownloadUrl = null;
        _pendingDownloadQueuedUtc = null;
    }

    private void ClearDownloaderJobStatus()
    {
        _downloaderJobStatus = null;
        RefreshDownloaderStatus();
    }

    private void ImportDownloaderFile(string path, string? fallbackTitle = null, bool fromExternalRequest = false) =>
        _ = ImportDownloaderFileAsync(path, fallbackTitle, fromExternalRequest);

    private async Task ImportDownloaderFileAsync(
        string path,
        string? fallbackTitle = null,
        bool fromExternalRequest = false)
    {
        try
        {
            if (!AudioTagReader.IsSupported(path))
                return;

            var track = await AudioFileAccess.ReadTrackWhenReadyAsync(path, DateTime.UtcNow).ConfigureAwait(true);
            if (track is null)
            {
                if (fromExternalRequest)
                    ViewTitleText.Text = "Import failed: file is still being written or is unreadable";
                return;
            }

            _repository.UpsertTrack(track);
            _scriptHooks.OnImport(path);
            _lyricsPrefetch.Enqueue(track);
            RefreshLibraryViews();
            var label = string.IsNullOrWhiteSpace(track.DisplayTitle) ? fallbackTitle : track.DisplayTitle;

            if (fromExternalRequest)
            {
                ViewTitleText.Text = $"Imported: {label ?? "track"}";
                if (IsVisible)
                    Activate();
            }
            else if (_pendingDownloadUrl is not null)
            {
                SetDownloaderJobStatus($"Added: {label ?? "track"}");
                _ = Task.Delay(TimeSpan.FromSeconds(8)).ContinueWith(_ =>
                    Dispatcher.Invoke(ClearDownloaderJobStatus));
            }
            else
            {
                ViewTitleText.Text = $"Added from downloader: {label ?? "track"}";
            }
        }
        catch (Exception ex)
        {
            if (AudioFileAccess.IsSharingViolation(ex))
                return;

            if (fromExternalRequest)
                ViewTitleText.Text = $"Import failed: {ex.Message}";
            else
                SetDownloaderJobStatus($"Error importing file: {ex.Message}");
        }
    }

    private void ImportAudioFile(string path, bool fromExternalRequest = false) =>
        ImportDownloaderFile(path, fromExternalRequest: fromExternalRequest);

    private void ImportAudioFolder(string folder, bool fromExternalRequest = false)
    {
        if (!Directory.Exists(folder))
        {
            if (fromExternalRequest)
                ViewTitleText.Text = "Import failed: album folder not found";
            return;
        }

        var imported = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder).Where(AudioTagReader.IsSupported).OrderBy(p => p))
            {
                try
                {
                    var track = AudioTagReader.Read(path, DateTime.UtcNow);
                    _repository.UpsertTrack(track);
                    _scriptHooks.OnImport(path);
                    _lyricsPrefetch.Enqueue(track);
                    imported++;
                }
                catch
                {
                    /* skip bad files */
                }
            }

            RefreshLibraryViews();

            if (fromExternalRequest)
            {
                ViewTitleText.Text = imported > 0
                    ? $"Imported album: {imported} track{(imported == 1 ? "" : "s")}"
                    : "No tracks imported from album folder";
                if (IsVisible)
                    Activate();
            }
            else
            {
                ViewTitleText.Text = imported > 0
                    ? $"Added album: {imported} track{(imported == 1 ? "" : "s")}"
                    : "No tracks added from album folder";
            }
        }
        catch (Exception ex)
        {
            if (fromExternalRequest)
                ViewTitleText.Text = $"Album import failed: {ex.Message}";
            else
                SetDownloaderJobStatus($"Error importing album: {ex.Message}");
        }
    }

    private void DownloaderBridge_OnNewAudioFileDetected(object? sender, string path)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await ImportDownloaderFileAsync(path).ConfigureAwait(true);
            if (_pendingDownloadUrl is not null)
                ClearPendingDownloadWatch();
        });
    }

    private async void ScanLibrary_OnClick(object sender, RoutedEventArgs e) =>
        await ScanLibraryAsync();

    private void CleanDead_OnClick(object sender, RoutedEventArgs e)
    {
        var removed = _repository.RemoveDeadEntries();
        RefreshLibraryViews();
        RefreshPlaylistNav();
        ViewTitleText.Text = removed == 0
            ? "No missing files found"
            : $"Removed {removed} missing file(s)";
    }

    private async Task ScanLibraryAsync()
    {
        if (_scanInProgress)
            return;

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        var roots = App.Settings.LibraryFolders.Where(Directory.Exists).Distinct().ToList();
        if (roots.Count == 0)
            roots.Add(AppPaths.DefaultMusicFolder);

        _scanInProgress = true;
        BrowseList.IsEnabled = false;
        PlaylistNavTree.IsEnabled = false;
        ViewTitleText.Text = "Scanning…";
        try
        {
            var result = await _scanner.ScanAsync(
                roots,
                new Progress<ScanProgress>(p =>
                {
                    ViewTitleText.Text = p.Total > 0
                        ? $"Scanning… {p.Done}/{p.Total}"
                        : "Scanning library…";
                }),
                token).ConfigureAwait(true);

            RefreshLibraryViews();
            RefreshPlaylistNav();
            ViewTitleText.Text = $"Scan complete — {result.Indexed} tracks indexed";
            _scriptHooks.OnScanComplete(result.Indexed);
            if (App.Settings.AutoDownloadLyrics)
                _lyricsPrefetch.EnqueueMany(_repository.GetAllTracks());
        }
        catch (OperationCanceledException)
        {
            ViewTitleText.Text = "Scan cancelled";
        }
        finally
        {
            _scanInProgress = false;
            BrowseList.IsEnabled = true;
            PlaylistNavTree.IsEnabled = true;
        }
    }

    private void Playback_OnTrackStarted(object? sender, LibraryTrack track)
    {
        if (track.Id <= 0)
            return;

        try
        {
            _repository.RecordPlay(track.Id);
            _lastFm.OnTrackStarted(track);
            _scriptHooks.OnTrackStarted(track);
            _lyricsPrefetch.Enqueue(track);
            Dispatcher.Invoke(() =>
            {
                RefreshNowPlaying();
                UpdateDiscordPresence();
                if (_browseMode == "home")
                    ShowHome();
            });
        }
        catch
        {
            /* ignore */
        }
    }

    private void BrowseList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressNavEvents)
            return;

        if (BrowseList.SelectedItem is not ListBoxItem item || item.Tag is not string mode)
            return;

        _selectedPlaylistId = null;
        _suppressNavEvents = true;
        ClearPlaylistTreeSelection();
        _suppressNavEvents = false;

        if (mode == "albums")
        {
            _listDrillDown = null;
            _listDrillDownKind = null;
            // Already inside an opened album → Albums means "back to grid".
            if (_browseMode == "album-tracks")
            {
                _albumDrillDown = null;
                _albumDrillDownMemory = null;
                _selectedAlbum = null;
                _browseMode = "albums";
            }
            // Returning from elsewhere → restore last opened album if any.
            else if (!string.IsNullOrWhiteSpace(_albumDrillDownMemory))
            {
                _browseMode = "album-tracks";
                _albumDrillDown = _albumDrillDownMemory;
            }
            else
            {
                _browseMode = "albums";
                _albumDrillDown = null;
            }
        }
        else
        {
            _browseMode = mode;
            _albumDrillDown = null;
            _listDrillDown = null;
            _listDrillDownKind = null;
            if (mode != "albums")
                _selectedAlbum = null;
            // Keep _albumDrillDownMemory so Albums can restore later.
            if (SearchBox is not null && mode is "artists" or "genres" or "tracks")
                SearchBox.Text = "";
        }

        RefreshLibraryViews();
        UpdateOpenButtonVisibility();
    }

    private void PlaylistNavTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (!IsLoaded || _suppressNavEvents)
            return;

        if (GetPlaylistFromNode(PlaylistNavTree.SelectedItem) is not LibraryPlaylist playlist)
            return;

        _browseMode = "playlist";
        _selectedPlaylistId = playlist.Id;
        _albumDrillDown = null;
        _selectedAlbum = null;
        _suppressNavEvents = true;
        BrowseList.SelectedItem = null;
        _suppressNavEvents = false;
        RefreshLibraryViews();
    }

    private void PlaylistNavTree_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GetPlaylistFromNode(PlaylistNavTree.SelectedItem) is not LibraryPlaylist playlist)
            return;

        var tracks = _repository.GetPlaylistTracks(playlist.Id);
        if (tracks.Count == 0)
            return;

        _playback.SetQueue(tracks, 0);
        _playback.Play();
    }

    private static LibraryPlaylist? GetPlaylistFromNode(object? item) =>
        item is PlaylistTreeNode { IsFolder: false, Playlist: { } playlist } ? playlist : null;

    private static PlaylistTreeNode? GetFolderFromNode(object? item) =>
        item is PlaylistTreeNode { IsFolder: true } folder ? folder : null;

    private static PlaylistTreeNode? FindPlaylistNode(IEnumerable<PlaylistTreeNode> nodes, long playlistId)
    {
        foreach (var node in nodes)
        {
            if (!node.IsFolder && node.Playlist?.Id == playlistId)
                return node;
            var child = FindPlaylistNode(node.Children, playlistId);
            if (child is not null)
                return child;
        }

        return null;
    }

    private void TrySelectTreeNode(PlaylistTreeNode? node)
    {
        if (node is null)
            return;

        PlaylistNavTree.UpdateLayout();
        if (FindTreeViewItem(PlaylistNavTree, node) is TreeViewItem item)
            item.IsSelected = true;
    }

    private static TreeViewItem? FindTreeViewItem(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem direct)
            return direct;

        foreach (var child in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(child) is not TreeViewItem container)
                continue;

            container.UpdateLayout();
            if (Equals(child, item))
                return container;

            var nested = FindTreeViewItem(container, item);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private void ClearPlaylistTreeSelection()
    {
        foreach (var item in PlaylistNavTree.Items)
        {
            if (PlaylistNavTree.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem tvi)
                ClearTreeViewItemSelection(tvi);
        }
    }

    private static void ClearTreeViewItemSelection(TreeViewItem item)
    {
        item.IsSelected = false;
        foreach (var child in item.Items)
        {
            if (item.ItemContainerGenerator.ContainerFromItem(child) is TreeViewItem childItem)
                ClearTreeViewItemSelection(childItem);
        }
    }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        if (_browseMode is "albums" or "queue" or "home")
            return;

        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void RefreshPlaylistNav()
    {
        var selectedId = _selectedPlaylistId;
        var tree = _repository.GetPlaylistTree();
        PlaylistNavTree.ItemsSource = tree;
        if (selectedId is long id)
        {
            _suppressNavEvents = true;
            TrySelectTreeNode(FindPlaylistNode(tree, id));
            _suppressNavEvents = false;
        }
    }

    private void RefreshLibraryViews()
    {
        if (TrackList is null || ViewTitleText is null || LibraryStatsText is null)
            return;

        var search = SearchBox?.Text?.Trim();
        try
        {
            ShowTrackList();
            HideAlbumDetailHeader();
            UpdateEmptyHint(false);

            switch (_browseMode)
            {
                case "home":
                    ViewTitleText.Text = "Home";
                    if (SearchBox is not null)
                        SearchBox.Visibility = Visibility.Collapsed;
                    ShowHome();
                    break;
                case "albums":
                    ViewTitleText.Text = "Albums";
                    if (SearchBox is not null)
                        SearchBox.Visibility = Visibility.Collapsed;
                    ShowAlbumGrid(_repository.GetAlbums());
                    break;
                case "artists":
                    ViewTitleText.Text = "Artists";
                    if (SearchBox is not null)
                    {
                        SearchBox.Visibility = Visibility.Visible;
                        SearchBox.ToolTip = "Filter artists by name";
                    }

                    ApplyListTemplate("artists");
                    var artists = _repository.GetArtists();
                    if (!string.IsNullOrWhiteSpace(search))
                    {
                        artists = artists.Where(a =>
                            a.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
                    }

                    TrackList.ItemsSource = artists;
                    if (artists.Count == 0)
                    {
                        UpdateEmptyHint(true, string.IsNullOrWhiteSpace(search)
                            ? "No artists in your library yet.\nScan a music folder to get started."
                            : "No artists match your search.");
                    }

                    break;
                case "genres":
                    ViewTitleText.Text = "Genres";
                    if (SearchBox is not null)
                    {
                        SearchBox.Visibility = Visibility.Visible;
                        SearchBox.ToolTip = "Filter genres by name";
                    }

                    ApplyListTemplate("genres");
                    var genres = _repository.GetGenres();
                    if (!string.IsNullOrWhiteSpace(search))
                    {
                        genres = genres.Where(g =>
                            g.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
                    }

                    TrackList.ItemsSource = genres;
                    if (genres.Count == 0)
                    {
                        UpdateEmptyHint(true, string.IsNullOrWhiteSpace(search)
                            ? "No genres tagged in your library yet.\nAdd genre tags or use MusicBrainz lookup."
                            : "No genres match your search.");
                    }

                    break;
                case "artist-tracks":
                case "genre-tracks":
                {
                    if (SearchBox is not null)
                    {
                        SearchBox.Visibility = Visibility.Visible;
                        SearchBox.ToolTip = "Search title, artist, album";
                    }

                    ApplyListTemplate("tracks");
                    var drilled = _browseMode == "artist-tracks"
                        ? _repository.GetAllTracks().Where(t =>
                            string.Equals(t.Artist, _listDrillDown, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(t.AlbumArtist, _listDrillDown, StringComparison.OrdinalIgnoreCase)).ToList()
                        : _repository.GetTracksForGenre(_listDrillDown ?? "").ToList();

                    if (!string.IsNullOrWhiteSpace(search))
                    {
                        drilled = drilled.Where(t =>
                            t.DisplayTitle.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            t.DisplayArtist.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            t.DisplayAlbum.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
                    }

                    drilled = ApplyQuickFilters(drilled);
                    TrackList.ItemsSource = drilled;
                    ShowListDrillDownHeader(
                        _browseMode == "artist-tracks" ? "ARTIST" : "GENRE",
                        _listDrillDown ?? "",
                        $"{drilled.Count} song{(drilled.Count == 1 ? "" : "s")}",
                        _browseMode == "artist-tracks" ? "← Artists" : "← Genres");
                    if (drilled.Count == 0)
                        UpdateEmptyHint(true, "No tracks in this view.");
                    break;
                }
                case "queue":
                    ViewTitleText.Text = "Play queue";
                    if (SearchBox is not null)
                        SearchBox.Visibility = Visibility.Collapsed;
                    ApplyListTemplate("tracks");
                    TrackList.ItemsSource = _playback.Queue.ToList();
                    break;
                case "playlist":
                    if (SearchBox is not null)
                        SearchBox.Visibility = Visibility.Visible;
                    ApplyListTemplate("tracks");
                    if (_selectedPlaylistId is long playlistId)
                    {
                        var playlist = _repository.GetPlaylists().FirstOrDefault(p => p.Id == playlistId);
                        ViewTitleText.Text = playlist is null ? "Playlist" : playlist.Name;
                        var playlistTracks = _repository.GetPlaylistTracks(playlistId).ToList();
                        if (!string.IsNullOrWhiteSpace(search))
                        {
                            playlistTracks = playlistTracks.Where(t =>
                                t.DisplayTitle.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                t.DisplayArtist.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                t.DisplayAlbum.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
                        }

                        playlistTracks = ApplyQuickFilters(playlistTracks);
                        TrackList.ItemsSource = playlistTracks;
                        if (playlist is { IsSmart: true } && playlistTracks.Count == 0 && string.IsNullOrWhiteSpace(search) && _quickFilters.Count == 0)
                        {
                            UpdateEmptyHint(true,
                                "No tracks match these rules yet.\nEdit the playlist rules or rate/play more of your library.");
                        }
                        else if (playlistTracks.Count == 0)
                        {
                            UpdateEmptyHint(true, "No tracks match your search or filters.");
                        }
                        else
                        {
                            UpdateEmptyHint(false);
                        }

                        if (playlist is not null)
                            ShowPlaylistDetailHeader(playlist, playlistTracks.Count);
                    }
                    else
                    {
                        ViewTitleText.Text = "Playlist";
                        TrackList.ItemsSource = Array.Empty<LibraryTrack>();
                    }

                    break;
                case "album-tracks":
                    if (SearchBox is not null)
                        SearchBox.Visibility = Visibility.Visible;
                    ApplyListTemplate("album-tracks");
                    if (!string.IsNullOrWhiteSpace(_albumDrillDown))
                    {
                        var albumTracks = _repository.GetTracksForAlbum(_albumDrillDown).ToList();
                        var first = albumTracks.FirstOrDefault();
                        ViewTitleText.Text = first is null
                            ? _albumDrillDown
                            : $"{first.DisplayAlbumArtist} — {first.DisplayAlbum}";
                        if (!string.IsNullOrWhiteSpace(search))
                        {
                            albumTracks = albumTracks.Where(t =>
                                t.DisplayTitle.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                t.DisplayArtist.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
                        }

                        albumTracks = ApplyQuickFilters(albumTracks);
                        TrackList.ItemsSource = albumTracks;
                        ShowAlbumDetailHeader(albumTracks);
                        if (albumTracks.Count == 0)
                            UpdateEmptyHint(true, "No tracks match your search or filters.");
                    }
                    else
                    {
                        _browseMode = "albums";
                        goto case "albums";
                    }

                    break;
                case "inbox":
                    ViewTitleText.Text = "Inbox";
                    if (SearchBox is not null)
                    {
                        SearchBox.Visibility = Visibility.Visible;
                        SearchBox.ToolTip = "Search inbox tracks";
                    }

                    ApplyListTemplate("tracks");
                    var inboxTracks = _repository.GetInboxTracks(search);
                    TrackList.ItemsSource = inboxTracks;
                    if (inboxTracks.Count == 0)
                    {
                        UpdateEmptyHint(true, string.IsNullOrWhiteSpace(search)
                            ? "No tracks waiting in the inbox.\nNew imports can be flagged automatically in Settings."
                            : "No inbox tracks match your search.");
                    }

                    break;
                default:
                    ViewTitleText.Text = string.IsNullOrWhiteSpace(search) ? "All tracks" : $"Search: {search}";
                    if (SearchBox is not null)
                    {
                        SearchBox.Visibility = Visibility.Visible;
                        SearchBox.ToolTip = "Search title, artist, album, genre";
                    }

                    ApplyListTemplate("tracks");
                    var allTracks = _repository.QueryTracks(search, BuildQuickFilterRules());
                    TrackList.ItemsSource = allTracks;
                    if (allTracks.Count == 0)
                    {
                        UpdateEmptyHint(true, string.IsNullOrWhiteSpace(search) && _quickFilters.Count == 0
                            ? "No tracks in your library yet.\nScan a music folder to get started."
                            : "No tracks match your search or filters.");
                    }

                    break;
            }

            LibraryStatsText.Text = $"{_repository.TrackCount()} tracks in library";
            RefreshNowPlaying();
            UpdateOpenButtonVisibility();
            UpdateQuickFilterChrome();
        }
        catch (Exception ex)
        {
            ViewTitleText.Text = "Library view failed";
            LibraryStatsText.Text = ex.Message;
        }
    }

    private void HideAlbumDetailHeader()
    {
        if (AlbumDetailHeader is not null)
            AlbumDetailHeader.Visibility = Visibility.Collapsed;
    }

    private void ShowPlaylistDetailHeader(LibraryPlaylist playlist, int trackCount)
    {
        if (AlbumDetailHeader is null)
            return;

        AlbumDetailHeader.Visibility = Visibility.Visible;
        if (BackToAlbumsButton is not null)
        {
            BackToAlbumsButton.Content = "← Playlists";
            BackToAlbumsButton.ToolTip = "Clear playlist selection";
        }

        if (AlbumDetailKindLabel is not null)
            AlbumDetailKindLabel.Text = playlist.IsSmart ? "SMART PLAYLIST" : "PLAYLIST";
        AlbumDetailTitle.Text = playlist.Name;
        AlbumDetailArtist.Text = "";
        AlbumDetailArtist.Visibility = Visibility.Collapsed;
        AlbumDetailMeta.Text = $"{trackCount} song{(trackCount == 1 ? "" : "s")}";
        AlbumDetailCover.Source = CoverArtHelper.ToBitmap(playlist.CoverArt, 720, centerCropSquare: true);
        if (AlbumPlayPauseButton is not null)
            AlbumPlayPauseButton.Content = "Play playlist";
        ViewTitleText.Text = playlist.Name;
        UpdatePlayPauseChrome();
    }

    private void ShowAlbumDetailHeader(IReadOnlyList<LibraryTrack> tracks)
    {
        if (AlbumDetailHeader is null)
            return;

        var first = tracks.FirstOrDefault();
        var cover = tracks.Select(t => t.CoverArt).FirstOrDefault(c => c is { Length: > 0 })
                    ?? first?.CoverArt;
        var artist = _selectedAlbum?.AlbumArtist
                     ?? first?.DisplayAlbumArtist
                     ?? "Unknown Artist";
        var album = first?.DisplayAlbum ?? _albumDrillDown ?? "Album";
        var year = tracks.Select(t => t.Year).Where(y => y is > 0).Select(y => y!.Value).DefaultIfEmpty().Min();

        AlbumDetailHeader.Visibility = Visibility.Visible;
        if (BackToAlbumsButton is not null)
        {
            BackToAlbumsButton.Content = "← All albums";
            BackToAlbumsButton.ToolTip = "Back to album grid";
        }

        if (AlbumDetailKindLabel is not null)
            AlbumDetailKindLabel.Text = "ALBUM";
        AlbumDetailTitle.Text = album;
        AlbumDetailArtist.Text = artist;
        AlbumDetailArtist.Visibility = Visibility.Visible;
        AlbumDetailMeta.Text = year > 0
            ? $"{tracks.Count} songs · {year}"
            : $"{tracks.Count} songs";
        AlbumDetailCover.Source = CoverArtHelper.ToBitmap(cover, 720, centerCropSquare: true);
        if (AlbumPlayPauseButton is not null)
            AlbumPlayPauseButton.Content = "Play album";
        ViewTitleText.Text = "Album";
        UpdatePlayPauseChrome();
    }

    private void ShowListDrillDownHeader(string kind, string title, string meta, string backLabel)
    {
        if (AlbumDetailHeader is null)
            return;

        AlbumDetailHeader.Visibility = Visibility.Visible;
        if (BackToAlbumsButton is not null)
        {
            BackToAlbumsButton.Content = backLabel;
            BackToAlbumsButton.ToolTip = backLabel.StartsWith("← Artists", StringComparison.Ordinal)
                ? "Back to artists"
                : "Back to genres";
        }

        if (AlbumDetailKindLabel is not null)
            AlbumDetailKindLabel.Text = kind;
        AlbumDetailTitle.Text = title;
        AlbumDetailArtist.Text = "";
        AlbumDetailArtist.Visibility = Visibility.Collapsed;
        AlbumDetailMeta.Text = meta;
        AlbumDetailCover.Source = null;
        if (AlbumPlayPauseButton is not null)
            AlbumPlayPauseButton.Content = "Play";
        ViewTitleText.Text = title;
        UpdatePlayPauseChrome();
    }

    private void BackToAlbums_OnClick(object sender, RoutedEventArgs e)
    {
        if (_browseMode is "artist-tracks" or "genre-tracks")
        {
            var target = _listDrillDownKind is "genres" ? "genres" : "artists";
            _listDrillDown = null;
            _listDrillDownKind = null;
            _browseMode = target;
            _selectedPlaylistId = null;
            _suppressNavEvents = true;
            ClearPlaylistTreeSelection();
            foreach (var item in BrowseList.Items.OfType<ListBoxItem>())
            {
                if (item.Tag is string tag && tag == target)
                {
                    BrowseList.SelectedItem = item;
                    break;
                }
            }

            _suppressNavEvents = false;
            if (SearchBox is not null)
                SearchBox.Text = "";
            RefreshLibraryViews();
            UpdateOpenButtonVisibility();
            return;
        }

        _albumDrillDown = null;
        _albumDrillDownMemory = null;
        _selectedAlbum = null;
        _browseMode = "albums";
        _selectedPlaylistId = null;
        _suppressNavEvents = true;
        ClearPlaylistTreeSelection();
        foreach (var item in BrowseList.Items.OfType<ListBoxItem>())
        {
            if (item.Tag is "albums")
            {
                BrowseList.SelectedItem = item;
                break;
            }
        }

        _suppressNavEvents = false;
        RefreshLibraryViews();
        UpdateOpenButtonVisibility();
    }

    private void ShowTrackList()
    {
        TrackList.Visibility = Visibility.Visible;
        AlbumGridScroller.Visibility = Visibility.Collapsed;
        if (HomeScroller is not null)
            HomeScroller.Visibility = Visibility.Collapsed;
    }

    private void UpdateEmptyHint(bool show, string? message = null)
    {
        if (EmptyHintText is null)
            return;

        EmptyHintText.Text = message ?? "";
        EmptyHintText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowAlbumGrid(IReadOnlyList<LibraryAlbum> albums)
    {
        TrackList.Visibility = Visibility.Collapsed;
        if (HomeScroller is not null)
            HomeScroller.Visibility = Visibility.Collapsed;
        AlbumGridScroller.Visibility = Visibility.Visible;
        var selectedKey = _selectedAlbum?.Key;
        foreach (var album in albums)
            album.IsSelected = selectedKey is not null &&
                               string.Equals(album.Key, selectedKey, StringComparison.Ordinal);
        if (selectedKey is not null)
            _selectedAlbum = albums.FirstOrDefault(a => a.Key == selectedKey);
        AlbumGrid.ItemsSource = null;
        AlbumGrid.ItemsSource = albums;
        UpdateAlbumCardPauseIndicators();
    }

    private void ShowHome()
    {
        TrackList.Visibility = Visibility.Collapsed;
        AlbumGridScroller.Visibility = Visibility.Collapsed;
        if (HomeScroller is null)
            return;

        HomeScroller.Visibility = Visibility.Visible;
        var recent = _repository.GetRecentlyPlayedAlbums(16);
        var added = _repository.GetRecentlyAddedAlbums(16);

        JumpBackInEmpty.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentlyAddedEmpty.Visibility = added.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var selectedKey = _selectedAlbum?.Key;
        foreach (var album in recent.Concat(added))
            album.IsSelected = selectedKey is not null &&
                               string.Equals(album.Key, selectedKey, StringComparison.Ordinal);

        JumpBackInGrid.ItemsSource = null;
        JumpBackInGrid.ItemsSource = recent;
        RecentlyAddedGrid.ItemsSource = null;
        RecentlyAddedGrid.ItemsSource = added;
        JumpBackInScroller?.ScrollToHorizontalOffset(0);
        RecentlyAddedScroller?.ScrollToHorizontalOffset(0);
        UpdateAlbumCardPauseIndicators();
        UpdateCarouselNav(JumpBackInCarousel, JumpBackInCarousel?.IsMouseOver == true);
        UpdateCarouselNav(RecentlyAddedCarousel, RecentlyAddedCarousel?.IsMouseOver == true);
    }

    private void Carousel_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement fe)
            UpdateCarouselNav(fe, hovering: true);
    }

    private void Carousel_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement fe)
            UpdateCarouselNav(fe, hovering: false);
    }

    private void Carousel_ForwardWheelVertical_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (HomeScroller is null)
            return;

        var next = Math.Clamp(HomeScroller.VerticalOffset - e.Delta, 0, HomeScroller.ScrollableHeight);
        HomeScroller.ScrollToVerticalOffset(next);
        e.Handled = true;
    }

    private void Carousel_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroller)
            return;
        var host = scroller.Name switch
        {
            "JumpBackInScroller" => (FrameworkElement?)JumpBackInCarousel,
            "RecentlyAddedScroller" => RecentlyAddedCarousel,
            _ => null
        };
        if (host is not null)
            UpdateCarouselNav(host, host.IsMouseOver);
    }

    private void CarouselPrev_OnClick(object sender, RoutedEventArgs e)
    {
        if (ResolveCarouselScroller(sender) is { } scroller)
            scroller.ScrollToHorizontalOffset(Math.Max(0, scroller.HorizontalOffset - CarouselStep(scroller)));
    }

    private void CarouselNext_OnClick(object sender, RoutedEventArgs e)
    {
        if (ResolveCarouselScroller(sender) is { } scroller)
        {
            var max = Math.Max(0, scroller.ExtentWidth - scroller.ViewportWidth);
            scroller.ScrollToHorizontalOffset(Math.Min(max, scroller.HorizontalOffset + CarouselStep(scroller)));
        }
    }

    private static double CarouselStep(ScrollViewer scroller) =>
        Math.Max(320, scroller.ViewportWidth * 0.85);

    private ScrollViewer? ResolveCarouselScroller(object sender)
    {
        if (sender is not FrameworkElement { Tag: string name })
            return null;
        return name switch
        {
            "JumpBackInScroller" => JumpBackInScroller,
            "RecentlyAddedScroller" => RecentlyAddedScroller,
            _ => null
        };
    }

    private void UpdateCarouselNav(FrameworkElement? carousel, bool hovering)
    {
        if (carousel is null)
            return;

        ScrollViewer? scroller;
        System.Windows.Controls.Button? left;
        System.Windows.Controls.Button? right;
        if (ReferenceEquals(carousel, JumpBackInCarousel))
        {
            scroller = JumpBackInScroller;
            left = JumpBackInLeftButton;
            right = JumpBackInRightButton;
        }
        else if (ReferenceEquals(carousel, RecentlyAddedCarousel))
        {
            scroller = RecentlyAddedScroller;
            left = RecentlyAddedLeftButton;
            right = RecentlyAddedRightButton;
        }
        else
            return;

        if (scroller is null || left is null || right is null)
            return;

        var canLeft = scroller.HorizontalOffset > 2;
        var canRight = scroller.ExtentWidth > scroller.ViewportWidth + 2 &&
                       scroller.HorizontalOffset + scroller.ViewportWidth < scroller.ExtentWidth - 2;

        left.Visibility = hovering && canLeft ? Visibility.Visible : Visibility.Collapsed;
        right.Visibility = hovering && canRight ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyListTemplate(string mode)
    {
        TrackList.DisplayMemberPath = null;
        TrackList.ItemTemplate = mode switch
        {
            "albums" => (DataTemplate)FindResource("AlbumListItemTemplate"),
            "artists" => (DataTemplate)FindResource("ArtistListItemTemplate"),
            "genres" => (DataTemplate)FindResource("GenreListItemTemplate"),
            "album-tracks" => (DataTemplate)FindResource("AlbumTrackListItemTemplate"),
            _ => (DataTemplate)FindResource("TrackListItemTemplate"),
        };
    }

    private void RefreshQueueList()
    {
        if (QueueList is null)
            return;

        var queue = _playback.Queue.ToList();
        QueueList.ItemsSource = queue;
        if (_playback.QueueIndex >= 0 && _playback.QueueIndex < queue.Count)
            QueueList.SelectedIndex = _playback.QueueIndex;

        QueueStatsText.Text = queue.Count == 0
            ? "Queue is empty"
            : $"{queue.Count} in queue · now #{Math.Max(1, _playback.QueueIndex + 1)}";

        if (_browseMode == "queue" && TrackList is not null)
        {
            ApplyListTemplate("tracks");
            TrackList.ItemsSource = queue;
        }
    }

    private void TrackList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        /* selection only */
    }

    private void TrackList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_browseMode is "artists" or "genres")
        {
            OpenSelection_OnClick(sender, e);
            return;
        }

        PlayCurrentSelection(replaceQueue: true);
    }

    private void AlbumGrid_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        // Hover play button handles its own Click — don't treat as select/open.
        if (FindAncestorWithTag(source, "AlbumPlay") is not null)
            return;

        var album = FindAlbumDataContext(source);
        if (album is null)
            return;

        // Double-click opens the album (Spotify/iTunes-style). Play via hover ▶.
        if (e.ClickCount >= 2)
        {
            SelectAlbum(album, refreshGrid: false);
            OpenAlbumTracks(album);
            e.Handled = true;
            return;
        }

        SelectAlbum(album, refreshGrid: false);
        e.Handled = true;
    }

    private void AlbumCardPlay_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LibraryAlbum album })
            return;

        SelectAlbum(album, refreshGrid: false);

        // Only this album toggles pause; other albums always start playback.
        if (IsAlbumNowPlaying(album.Album) && _playback.IsPlaying)
        {
            _playback.Pause();
            UpdatePlayPauseChrome();
            e.Handled = true;
            return;
        }

        if (IsAlbumNowPlaying(album.Album) && _playback.IsPaused)
        {
            _playback.Play();
            UpdatePlayPauseChrome();
            e.Handled = true;
            return;
        }

        PlayAlbum(album);
        e.Handled = true;
    }

    private void PlayAlbum(LibraryAlbum album)
    {
        var tracks = _repository.GetTracksForAlbum(album.Album);
        if (tracks.Count == 0)
            return;
        _playback.SetQueue(tracks, 0);
        _playback.Play();
    }

    private static DependencyObject? FindAncestorWithTag(DependencyObject? node, string tag)
    {
        while (node is not null)
        {
            if (node is FrameworkElement { Tag: string t } &&
                string.Equals(t, tag, StringComparison.Ordinal))
                return node;
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }

        return null;
    }

    private void SelectAlbum(LibraryAlbum album, bool refreshGrid = false)
    {
        foreach (var item in EnumerateVisibleAlbums())
            item.IsSelected = string.Equals(item.Key, album.Key, StringComparison.Ordinal);

        album.IsSelected = true;
        _selectedAlbum = album;
        ViewTitleText.Text = _browseMode == "home"
            ? $"Home · {album.DisplayLabel}  (double-click open · ▶ to play)"
            : $"Albums · {album.DisplayLabel}  (double-click open · ▶ to play)";

        if (refreshGrid)
            RefreshVisibleAlbumSources();
    }

    private IEnumerable<LibraryAlbum> EnumerateVisibleAlbums()
    {
        if (AlbumGrid?.ItemsSource is IEnumerable<LibraryAlbum> albums)
        {
            foreach (var a in albums)
                yield return a;
        }

        if (JumpBackInGrid?.ItemsSource is IEnumerable<LibraryAlbum> jump)
        {
            foreach (var a in jump)
                yield return a;
        }

        if (RecentlyAddedGrid?.ItemsSource is IEnumerable<LibraryAlbum> added)
        {
            foreach (var a in added)
                yield return a;
        }
    }

    private void RefreshVisibleAlbumSources()
    {
        if (AlbumGrid?.ItemsSource is IEnumerable<LibraryAlbum> list)
        {
            var copy = list.ToList();
            AlbumGrid.ItemsSource = null;
            AlbumGrid.ItemsSource = copy;
        }

        if (JumpBackInGrid?.ItemsSource is IEnumerable<LibraryAlbum> jump)
        {
            var copy = jump.ToList();
            JumpBackInGrid.ItemsSource = null;
            JumpBackInGrid.ItemsSource = copy;
        }

        if (RecentlyAddedGrid?.ItemsSource is IEnumerable<LibraryAlbum> added)
        {
            var copy = added.ToList();
            RecentlyAddedGrid.ItemsSource = null;
            RecentlyAddedGrid.ItemsSource = copy;
        }
    }

    private void OpenAlbumTracks(LibraryAlbum album)
    {
        _browseMode = "album-tracks";
        _albumDrillDown = album.Album;
        _albumDrillDownMemory = album.Album;
        _selectedAlbum = album;
        _selectedPlaylistId = null;
        _suppressNavEvents = true;
        BrowseList.SelectedItem = null;
        ClearPlaylistTreeSelection();
        _suppressNavEvents = false;
        RefreshLibraryViews();
    }

    private void OpenSelection_OnClick(object sender, RoutedEventArgs e)
    {
        if (_browseMode is "albums" or "home")
        {
            var album = _selectedAlbum;
            if (album is null)
            {
                ViewTitleText.Text = _browseMode == "home"
                    ? "Home · select an album first"
                    : "Albums · select an album first";
                return;
            }

            OpenAlbumTracks(album);
            return;
        }

        if (_browseMode == "artists" && TrackList.SelectedItem is LibraryArtist artist)
        {
            _listDrillDown = artist.Name;
            _listDrillDownKind = "artists";
            _browseMode = "artist-tracks";
            if (SearchBox is not null)
                SearchBox.Text = "";
            RefreshLibraryViews();
            UpdateOpenButtonVisibility();
            return;
        }

        if (_browseMode == "genres" && TrackList.SelectedItem is LibraryGenre genre)
        {
            _listDrillDown = genre.Name;
            _listDrillDownKind = "genres";
            _browseMode = "genre-tracks";
            if (SearchBox is not null)
                SearchBox.Text = "";
            RefreshLibraryViews();
            UpdateOpenButtonVisibility();
            return;
        }

        if (_browseMode is "artists" or "genres")
            ViewTitleText.Text = _browseMode == "artists"
                ? "Artists · select an artist first"
                : "Genres · select a genre first";
    }

    private void UpdateOpenButtonVisibility()
    {
        if (OpenSelectionButton is null)
            return;

        OpenSelectionButton.Visibility = _browseMode is "home" or "albums" or "artists" or "genres"
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void QuickFilter_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string tag })
            return;

        if (!_quickFilters.Add(tag))
            _quickFilters.Remove(tag);

        RefreshLibraryViews();
    }

    private void QuickFilterClear_OnClick(object sender, RoutedEventArgs e)
    {
        if (_quickFilters.Count == 0)
            return;
        _quickFilters.Clear();
        RefreshLibraryViews();
    }

    private SmartPlaylistRules? BuildQuickFilterRules()
    {
        if (_quickFilters.Count == 0)
            return null;

        var rules = new SmartPlaylistRules { MatchMode = "all" };
        foreach (var key in _quickFilters)
        {
            SmartPlaylistRule? rule = key switch
            {
                "never_played" => new SmartPlaylistRule { Field = "never_played", Operator = "is_true", Value = "" },
                "unrated" => new SmartPlaylistRule { Field = "unrated", Operator = "is_true", Value = "" },
                "rated4" => new SmartPlaylistRule { Field = "rating", Operator = "min", Value = "4" },
                "flac" => new SmartPlaylistRule { Field = "format", Operator = "equals", Value = "FLAC" },
                "recent" => new SmartPlaylistRule { Field = "date_added", Operator = "last_days", Value = "30" },
                _ => null,
            };
            if (rule is not null)
                rules.Rules.Add(rule);
        }

        return rules.Rules.Count == 0 ? null : rules;
    }

    private List<LibraryTrack> ApplyQuickFilters(List<LibraryTrack> tracks)
    {
        if (_quickFilters.Count == 0)
            return tracks;

        return tracks.Where(MatchesQuickFilters).ToList();
    }

    private bool MatchesQuickFilters(LibraryTrack t)
    {
        foreach (var key in _quickFilters)
        {
            var ok = key switch
            {
                "never_played" => t.PlayCount == 0,
                "unrated" => t.Rating == 0,
                "rated4" => t.Rating >= 4,
                "flac" => t.Format.Equals("FLAC", StringComparison.OrdinalIgnoreCase),
                "recent" => t.DateAddedUtc >= DateTime.UtcNow.AddDays(-30),
                _ => true,
            };
            if (!ok)
                return false;
        }

        return true;
    }

    private void UpdateQuickFilterChrome()
    {
        if (QuickFilterPanel is null)
            return;

        var show = _browseMode is not ("home" or "albums" or "artists" or "genres" or "queue");
        QuickFilterPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SetFilterButtonChrome(FilterNeverPlayedButton, "never_played");
        SetFilterButtonChrome(FilterUnratedButton, "unrated");
        SetFilterButtonChrome(FilterHighRatedButton, "rated4");
        SetFilterButtonChrome(FilterFlacButton, "flac");
        SetFilterButtonChrome(FilterRecentButton, "recent");
        if (FilterClearButton is not null)
            FilterClearButton.IsEnabled = _quickFilters.Count > 0;
    }

    private void SetFilterButtonChrome(System.Windows.Controls.Button? button, string key)
    {
        if (button is null)
            return;
        var on = _quickFilters.Contains(key);
        button.Opacity = on ? 1.0 : 0.75;
        button.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        try
        {
            button.Style = (Style)FindResource(on ? "HubPrimaryButton" : "HubToolbarButton");
        }
        catch
        {
            /* theme resource missing */
        }
    }

    private static LibraryAlbum? FindAlbumDataContext(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is FrameworkElement { DataContext: LibraryAlbum album })
                return album;
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }

        return null;
    }

    private void PlaySelection_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsSelectionContextNowPlaying())
        {
            _playback.TogglePlayPause();
            UpdatePlayPauseChrome();
            return;
        }

        PlayCurrentSelection(replaceQueue: true);
        UpdatePlayPauseChrome();
    }

    /// <summary>
    /// Album header: Play album ↔ Pause only while this album is the active context.
    /// </summary>
    private void AlbumPlayPause_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsViewingAlbumNowPlaying() || IsViewingListDrillDownNowPlaying())
        {
            _playback.TogglePlayPause();
            UpdatePlayPauseChrome();
            return;
        }

        PlayCurrentSelection(replaceQueue: true);
        UpdatePlayPauseChrome();
    }

    private bool IsAlbumNowPlaying(string albumTitle)
    {
        var current = _playback.CurrentTrack;
        if (current is null || string.IsNullOrWhiteSpace(albumTitle))
            return false;

        return string.Equals(
            LibraryRepository.NormalizeAlbumKey(albumTitle),
            LibraryRepository.NormalizeAlbumKey(current.Album ?? ""),
            StringComparison.Ordinal);
    }

    private bool IsViewingAlbumNowPlaying() =>
        _browseMode == "album-tracks" &&
        !string.IsNullOrWhiteSpace(_albumDrillDown) &&
        IsAlbumNowPlaying(_albumDrillDown);

    private bool IsViewingListDrillDownNowPlaying()
    {
        if (_playback.CurrentTrack is null || string.IsNullOrWhiteSpace(_listDrillDown))
            return false;

        if (_browseMode == "artist-tracks")
        {
            var t = _playback.CurrentTrack;
            return string.Equals(t.Artist, _listDrillDown, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(t.AlbumArtist, _listDrillDown, StringComparison.OrdinalIgnoreCase);
        }

        if (_browseMode == "genre-tracks")
        {
            return GenreNormalizer.ContainsGenre(_playback.CurrentTrack.Genre, _listDrillDown);
        }

        return false;
    }

    private bool IsSelectionContextNowPlaying()
    {
        if (_playback.CurrentTrack is null)
            return false;

        if (_browseMode == "album-tracks")
            return IsViewingAlbumNowPlaying();

        if (_browseMode is "artist-tracks" or "genre-tracks")
            return IsViewingListDrillDownNowPlaying();

        if ((_browseMode is "albums" or "home") && _selectedAlbum is not null)
            return IsAlbumNowPlaying(_selectedAlbum.Album);

        // Track / playlist / artist lists: pause if the current track is in this view's playable set.
        var tracks = ResolveTracksForPlayback();
        var current = _playback.CurrentTrack;
        return tracks.Any(t => t.Id == current.Id ||
            string.Equals(t.FilePath, current.FilePath, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdatePlayPauseChrome()
    {
        SetPlayPauseIcons(_playback.IsPlaying);
        UpdateAlbumPlayPauseButton();
        UpdateToolbarPlayPauseButton();
        UpdateAlbumCardPauseIndicators();
    }

    private void SetPlayPauseIcons(bool playing)
    {
        if (PlayPausePlayIcon is not null)
            PlayPausePlayIcon.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        if (PlayPausePauseIcon is not null)
            PlayPausePauseIcon.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        if (PlayPauseButton is not null)
            PlayPauseButton.ToolTip = playing ? "Pause" : "Play";
    }

    private void UpdateAlbumPlayPauseButton()
    {
        if (AlbumPlayPauseButton is null)
            return;

        if ((IsViewingAlbumNowPlaying() || IsViewingListDrillDownNowPlaying()) && _playback.IsPlaying)
            AlbumPlayPauseButton.Content = "Pause";
        else if (_browseMode is "artist-tracks" or "genre-tracks")
            AlbumPlayPauseButton.Content = "Play";
        else
            AlbumPlayPauseButton.Content = "Play album";
    }

    private void UpdateToolbarPlayPauseButton()
    {
        if (PlaySelectionButton is null)
            return;

        if (IsSelectionContextNowPlaying() && _playback.IsPlaying)
            PlaySelectionButton.Content = "Pause";
        else
            PlaySelectionButton.Content = "Play";
    }

    private void UpdateAlbumCardPauseIndicators()
    {
        var current = _playback.CurrentTrack;
        var playingKey = current is null || !_playback.IsPlaying
            ? null
            : LibraryRepository.NormalizeAlbumKey(current.Album ?? "");

        foreach (var album in EnumerateVisibleAlbums())
            album.ShowPause = playingKey is not null &&
                              string.Equals(album.Key, playingKey, StringComparison.Ordinal);
    }

    private void AddSelectionToQueue_OnClick(object sender, RoutedEventArgs e)
    {
        var tracks = ResolveSelectedTracks();
        if (tracks.Count == 0)
            return;
        _playback.AddToQueue(tracks);
    }

    private void PlayNext_OnClick(object sender, RoutedEventArgs e)
    {
        var tracks = ResolveSelectedTracks();
        if (tracks.Count == 0)
            return;
        _playback.InsertAfterCurrent(tracks);
        ViewTitleText.Text = tracks.Count == 1
            ? $"Queued next: {tracks[0].DisplayTitle}"
            : $"Queued next: {tracks.Count} tracks";
    }

    private void PlayCurrentSelection(bool replaceQueue)
    {
        var tracks = ResolveTracksForPlayback();
        if (tracks.Count == 0)
        {
            if (_browseMode is "albums" or "home")
                ViewTitleText.Text = _browseMode == "home"
                    ? "Home · select an album first"
                    : "Albums · select an album first";
            return;
        }

        if (replaceQueue)
        {
            var start = 0;
            if (TrackList.SelectedItem is LibraryTrack selected)
            {
                var idx = tracks.FindIndex(t => t.Id == selected.Id ||
                    string.Equals(t.FilePath, selected.FilePath, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    start = idx;
            }

            _playback.SetQueue(tracks, start);
            _playback.Play();
        }
    }

    private List<LibraryTrack> ResolveTracksForPlayback()
    {
        if (_browseMode == "queue")
            return _playback.Queue.ToList();

        if (_browseMode is "albums" or "home")
        {
            var album = _selectedAlbum;
            if (album is not null)
                return _repository.GetTracksForAlbum(album.Album).ToList();
            return [];
        }

        if (_browseMode == "album-tracks" && !string.IsNullOrWhiteSpace(_albumDrillDown))
            return _repository.GetTracksForAlbum(_albumDrillDown).ToList();

        if (_browseMode == "artist-tracks" && !string.IsNullOrWhiteSpace(_listDrillDown))
        {
            return _repository.GetAllTracks().Where(t =>
                string.Equals(t.Artist, _listDrillDown, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.AlbumArtist, _listDrillDown, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (_browseMode == "genre-tracks" && !string.IsNullOrWhiteSpace(_listDrillDown))
            return _repository.GetTracksForGenre(_listDrillDown).ToList();

        if (TrackList.SelectedItem is LibraryAlbum selectedAlbum)
            return _repository.GetTracksForAlbum(selectedAlbum.Album).ToList();

        if (TrackList.SelectedItem is LibraryArtist artist)
            return _repository.GetAllTracks().Where(t =>
                string.Equals(t.Artist, artist.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.AlbumArtist, artist.Name, StringComparison.OrdinalIgnoreCase)).ToList();

        if (TrackList.SelectedItem is LibraryGenre genre)
            return _repository.GetTracksForGenre(genre.Name).ToList();

        if (TrackList.ItemsSource is IEnumerable<LibraryTrack> allTracks)
            return allTracks.ToList();

        return ResolveSelectedTracks();
    }

    private List<LibraryTrack> ResolveSelectedTracks()
    {
        if (_browseMode is "albums" or "home")
        {
            var album = _selectedAlbum;
            if (album is not null)
                return _repository.GetTracksForAlbum(album.Album).ToList();
            return [];
        }

        if (TrackList.SelectedItems.Count > 0)
        {
            var multi = TrackList.SelectedItems.OfType<LibraryTrack>().ToList();
            if (multi.Count > 0)
                return multi;
        }

        if (TrackList.SelectedItem is LibraryTrack track)
            return [track];

        if (TrackList.SelectedItem is LibraryAlbum selectedAlbum)
            return _repository.GetTracksForAlbum(selectedAlbum.Album).ToList();

        if (TrackList.SelectedItem is LibraryArtist artist)
            return _repository.GetAllTracks().Where(t =>
                string.Equals(t.Artist, artist.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.AlbumArtist, artist.Name, StringComparison.OrdinalIgnoreCase)).ToList();

        if (TrackList.SelectedItem is LibraryGenre genre)
            return _repository.GetTracksForGenre(genre.Name).ToList();

        if (_browseMode == "playlist" && _selectedPlaylistId is long playlistId &&
            TrackList.SelectedItem is null)
            return _repository.GetPlaylistTracks(playlistId).ToList();

        if (_browseMode == "album-tracks" && !string.IsNullOrWhiteSpace(_albumDrillDown) &&
            TrackList.SelectedItem is null)
            return _repository.GetTracksForAlbum(_albumDrillDown).ToList();

        if (_browseMode == "artist-tracks" && !string.IsNullOrWhiteSpace(_listDrillDown) &&
            TrackList.SelectedItem is null)
        {
            return _repository.GetAllTracks().Where(t =>
                string.Equals(t.Artist, _listDrillDown, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.AlbumArtist, _listDrillDown, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (_browseMode == "genre-tracks" && !string.IsNullOrWhiteSpace(_listDrillDown) &&
            TrackList.SelectedItem is null)
            return _repository.GetTracksForGenre(_listDrillDown).ToList();

        if (TrackList.ItemsSource is IEnumerable<LibraryTrack> tracks)
            return tracks.Take(1).ToList();

        return [];
    }

    private LibraryTrack? ResolveSingleSelectedTrack()
    {
        if (TrackList.SelectedItem is LibraryTrack track)
            return track;
        if (QueueList.IsKeyboardFocusWithin && QueueList.SelectedItem is LibraryTrack q)
            return q;
        return null;
    }

    private void ContextPlay_OnClick(object sender, RoutedEventArgs e) =>
        PlayCurrentSelection(replaceQueue: true);

    private void LibraryContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        var playlist = _selectedPlaylistId is long pid ? _repository.GetPlaylist(pid) : null;
        var removeItem = menu.Items.OfType<MenuItem>()
            .FirstOrDefault(m => (m.Header as string) == "Remove from playlist");
        if (removeItem is not null)
        {
            removeItem.Visibility =
                _browseMode == "playlist" && playlist is { IsSmart: false } && TrackList.SelectedItem is LibraryTrack
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        var tracks = ResolveSelectedTracks();
        var editTagsItem = menu.Items.OfType<MenuItem>()
            .FirstOrDefault(m => (m.Header as string)?.StartsWith("Edit tags", StringComparison.Ordinal) == true);
        if (editTagsItem is not null)
        {
            editTagsItem.Header = tracks.Count > 1
                ? $"Edit tags ({tracks.Count} selected)…"
                : "Edit tags…";
        }

        var inboxItem = menu.Items.OfType<MenuItem>()
            .FirstOrDefault(m => (m.Header as string) == "Mark inbox done");
        if (inboxItem is not null)
        {
            inboxItem.Visibility = tracks.Count > 0 &&
                                   tracks.Any(t => string.Equals(t.ReviewStatus, "inbox", StringComparison.OrdinalIgnoreCase))
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void ContextAddToQueue_OnClick(object sender, RoutedEventArgs e)
    {
        var tracks = ResolveSelectedTracks();
        if (tracks.Count > 0)
            _playback.AddToQueue(tracks);
    }

    private void ContextPlayNext_OnClick(object sender, RoutedEventArgs e)
    {
        var tracks = ResolveSelectedTracks();
        if (tracks.Count > 0)
            _playback.InsertAfterCurrent(tracks);
    }

    private void ContextShowInExplorer_OnClick(object sender, RoutedEventArgs e)
    {
        var track = ResolveSingleSelectedTrack() ?? _playback.CurrentTrack;
        if (track is null || string.IsNullOrWhiteSpace(track.AudioFilePath))
        {
            ViewTitleText.Text = "No file to show in Explorer";
            return;
        }

        try
        {
            if (!File.Exists(track.AudioFilePath))
            {
                ViewTitleText.Text = "File not found on disk";
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{track.AudioFilePath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Show in Explorer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ContextRemoveFromPlaylist_OnClick(object sender, RoutedEventArgs e)
    {
        if (_browseMode != "playlist" || _selectedPlaylistId is not long playlistId)
            return;
        if (_repository.GetPlaylist(playlistId) is { IsSmart: true })
            return;
        if (TrackList.SelectedItem is not LibraryTrack track)
            return;

        _repository.RemoveTrackFromPlaylist(playlistId, track.Id);
        RefreshPlaylistNav();
        RefreshLibraryViews();
        ViewTitleText.Text = $"Removed “{track.DisplayTitle}” from playlist";
    }

    private void ContextAddToPlaylist_OnClick(object sender, RoutedEventArgs e)
    {
        var tracks = ResolveSelectedTracks();
        if (tracks.Count == 0)
            return;

        var playlists = _repository.GetPlaylists();
        var manualPlaylists = playlists.Where(p => !p.IsSmart).ToList();
        if (manualPlaylists.Count == 0 && playlists.Count == 0)
        {
            NewPlaylist_OnClick(sender, e);
            playlists = _repository.GetPlaylists();
            manualPlaylists = playlists.Where(p => !p.IsSmart).ToList();
            if (manualPlaylists.Count == 0)
                return;
        }

        var names = manualPlaylists.Select(p => p.Name).ToArray();
        var prompt = new TextPromptWindow("Add to playlist", "Playlist name (existing or new):", names.FirstOrDefault() ?? "")
        {
            Owner = this,
        };
        if (prompt.ShowDialog() != true || string.IsNullOrWhiteSpace(prompt.Result))
            return;

        var existing = manualPlaylists.FirstOrDefault(p =>
            string.Equals(p.Name, prompt.Result, StringComparison.OrdinalIgnoreCase));
        var playlist = existing ?? _repository.CreatePlaylist(prompt.Result);
        _repository.AddTracksToPlaylist(playlist.Id, tracks.Select(t => t.Id));
        RefreshPlaylistNav();
        if (_browseMode == "playlist" && _selectedPlaylistId == playlist.Id)
            RefreshLibraryViews();
        ViewTitleText.Text = $"Added {tracks.Count} to “{playlist.Name}”";
    }

    private LyricsWindow? _lyricsWindow;

    private void Lyrics_OnClick(object sender, RoutedEventArgs e)
    {
        var track = _playback.CurrentTrack ?? ResolveSingleSelectedTrack();
        if (track is null)
        {
            ViewTitleText.Text = "Play or select a track to show lyrics";
            return;
        }

        if (_lyricsWindow is { IsLoaded: true })
        {
            _lyricsWindow.ShowTrack(track);
            _lyricsWindow.Activate();
            return;
        }

        _lyricsWindow = new LyricsWindow(track, () => _playback.Position) { Owner = this };
        _lyricsWindow.Closed += (_, _) => _lyricsWindow = null;
        _lyricsWindow.Show();
    }

    private void ExportPlaylistM3u_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetPlaylistFromNode(PlaylistNavTree.SelectedItem) is not LibraryPlaylist playlist)
        {
            MessageBox.Show(this, "Select a playlist first.", "Export M3U", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var tracks = _repository.GetPlaylistTracks(playlist.Id);
        if (tracks.Count == 0)
        {
            MessageBox.Show(this, "That playlist has no tracks.", "Export M3U", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export playlist as M3U",
            Filter = "M3U playlist (*.m3u;*.m3u8)|*.m3u;*.m3u8|All files (*.*)|*.*",
            FileName = $"{SanitizeFileName(playlist.Name)}.m3u",
        };
        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            M3uPlaylistService.Export(dlg.FileName, playlist.Name, tracks);
            ViewTitleText.Text = $"Exported “{playlist.Name}” ({tracks.Count} tracks)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportPlaylistM3u_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import M3U playlist",
            Filter = "M3U playlist (*.m3u;*.m3u8)|*.m3u;*.m3u8|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            var playlist = M3uPlaylistService.Import(_repository, dlg.FileName);
            RefreshPlaylistNav();
            ViewTitleText.Text = $"Imported playlist “{playlist.Name}”";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "playlist" : name.Trim();
    }

    private void VolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressVolumeSlider || !IsLoaded)
            return;

        _muted = false;
        _playback.SetVolume(VolumeSlider.Value);
        UpdateMuteChrome();
    }

    private void SyncSpeedComboFromSettings()
    {
        if (SpeedCombo is null)
            return;

        _suppressSpeedCombo = true;
        var speed = App.Settings.PlaybackSpeed;
        ComboBoxItem? best = null;
        var bestDelta = double.MaxValue;
        foreach (var item in SpeedCombo.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is not string tag || !double.TryParse(tag, out var value))
                continue;
            var delta = Math.Abs(value - speed);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = item;
            }
        }

        SpeedCombo.SelectedItem = best;
        _suppressSpeedCombo = false;
    }

    private void SpeedCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSpeedCombo || !IsLoaded || SpeedCombo?.SelectedItem is not ComboBoxItem item)
            return;
        if (item.Tag is not string tag || !double.TryParse(tag, out var speed))
            return;

        App.Settings.PlaybackSpeed = Math.Clamp(speed, 0.5, 2.0);
        _playback.ReloadOutputSettings();
    }

    private void Mute_OnClick(object sender, RoutedEventArgs e)
    {
        if (_muted)
        {
            _muted = false;
            _suppressVolumeSlider = true;
            _playback.SetVolume(_volumeBeforeMute);
            if (VolumeSlider is not null)
                VolumeSlider.Value = _volumeBeforeMute;
            _suppressVolumeSlider = false;
        }
        else
        {
            _volumeBeforeMute = Math.Max(0.05, _playback.Volume);
            _muted = true;
            _suppressVolumeSlider = true;
            _playback.SetVolume(0);
            if (VolumeSlider is not null)
                VolumeSlider.Value = 0;
            _suppressVolumeSlider = false;
        }

        UpdateMuteChrome();
    }

    private void AdjustVolume(double delta)
    {
        _muted = false;
        var next = Math.Clamp(_playback.Volume + delta, 0, 1);
        _suppressVolumeSlider = true;
        _playback.SetVolume(next);
        if (VolumeSlider is not null)
            VolumeSlider.Value = next;
        _suppressVolumeSlider = false;
        UpdateMuteChrome();
    }

    private void UpdateMuteChrome()
    {
        if (MuteButton is null)
            return;
        MuteButton.Content = _playback.Volume <= 0.001 || _muted ? "🔇" : "🔊";
    }

    private void SleepTimer_OnClick(object sender, RoutedEventArgs e)
    {
        if (_sleepTimer is null)
            return;

        var menu = new ContextMenu();
        void AddOption(string header, int minutes)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) =>
            {
                App.Settings.SleepTimerMinutes = minutes;
                App.SaveSettings();
                _sleepTimer.Start(TimeSpan.FromMinutes(minutes), _playback.Volume);
                UpdateSleepTimerChrome();
            };
            menu.Items.Add(item);
        }

        AddOption("15 minutes", 15);
        AddOption("30 minutes", 30);
        AddOption("45 minutes", 45);
        AddOption("60 minutes", 60);
        AddOption("90 minutes", 90);
        menu.Items.Add(new Separator());
        var endTrack = new MenuItem { Header = "End of current track" };
        endTrack.Click += (_, _) =>
        {
            _sleepTimer.StartEndOfTrack(_playback.Volume);
            UpdateSleepTimerChrome();
        };
        menu.Items.Add(endTrack);
        var endQueue = new MenuItem { Header = "End of queue" };
        endQueue.Click += (_, _) =>
        {
            _sleepTimer.StartEndOfQueue(_playback.Volume);
            UpdateSleepTimerChrome();
        };
        menu.Items.Add(endQueue);
        menu.Items.Add(new Separator());
        var cancel = new MenuItem { Header = "Cancel timer", IsEnabled = _sleepTimer.IsActive };
        cancel.Click += (_, _) =>
        {
            _sleepTimer.Cancel();
            UpdateSleepTimerChrome();
        };
        menu.Items.Add(cancel);
        menu.PlacementTarget = SleepTimerButton;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }

    private void UpdateSleepTimerChrome()
    {
        if (SleepTimerButton is null || _sleepTimer is null)
            return;
        SleepTimerButton.Content = _sleepTimer.IsActive ? _sleepTimer.StatusLabel : "Sleep";
        SleepTimerButton.ToolTip = _sleepTimer.IsActive
            ? "Sleep timer active — click to change or cancel"
            : "Sleep timer (fade out then pause)";
    }

    private void MiniPlayer_OnClick(object sender, RoutedEventArgs e)
    {
        if (_miniPlayer is { IsLoaded: true })
        {
            _miniPlayer.Activate();
            return;
        }

        _miniPlayer = new MiniPlayerWindow(_playback, () => NowPlayingCover.Source)
        {
            Owner = this,
            Left = Left + Width - 480,
            Top = Top + Height - 200,
        };
        _miniPlayer.Closed += (_, _) => _miniPlayer = null;
        _miniPlayer.Show();
    }

    private void NotifyTrayTrackChanged()
    {
        var track = _playback.CurrentTrack;
        if (track is null)
        {
            _tray.UpdateTooltip(null, null);
            return;
        }

        _tray.UpdateTooltip(track.DisplayTitle, track.DisplayArtist);
        _tray.NotifyTrackChanged(track.DisplayTitle, track.DisplayArtist);
    }

    private void ContextMusicBrainz_OnClick(object sender, RoutedEventArgs e)
    {
        var track = ResolveSingleSelectedTrack();
        if (track is null)
            return;

        var dlg = new MusicBrainzTagWindow(track) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null)
            return;

        try
        {
            var released = _playback.ReleaseCurrentFileIfMatches(track.FilePath);
            AudioTagWriter.Write(dlg.Result);
            _repository.UpdateTrackMetadata(dlg.Result);
            if (released is { } state)
                _playback.ResumeAfterFileRelease(state.WasPlaying, state.Position);

            RefreshLibraryViews();
            RefreshQueueList();
            RefreshNowPlaying();
            ViewTitleText.Text = $"MusicBrainz tags applied: {dlg.Result.DisplayTitle}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not apply MusicBrainz tags:\n{ex.Message}", "MusicBrainz",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ContextEditTags_OnClick(object sender, RoutedEventArgs e)
    {
        var tracks = ResolveSelectedTracks();
        if (tracks.Count == 0)
            return;

        if (tracks.Count == 1)
            EditTrackTags(tracks[0]);
        else
            _ = EditBatchTagsAsync(tracks);
    }

    private void ContextEditAlbum_OnClick(object sender, RoutedEventArgs e) =>
        EditAlbum_OnClick(sender, e);

    private void ContextDownloadAlbumLyrics_OnClick(object sender, RoutedEventArgs e) =>
        DownloadAlbumLyrics_OnClick(sender, e);

    private void DownloadAlbumLyrics_OnClick(object sender, RoutedEventArgs e)
    {
        var tracks = ResolveAlbumTracksForLyrics();
        if (tracks.Count == 0)
        {
            ViewTitleText.Text = "Select an album first";
            return;
        }

        StartLyricsDownload(tracks, $"album ({tracks.Count} tracks)");
    }

    private List<LibraryTrack> ResolveAlbumTracksForLyrics()
    {
        if (_browseMode == "album-tracks" && !string.IsNullOrWhiteSpace(_albumDrillDown))
            return _repository.GetTracksForAlbum(_albumDrillDown).ToList();
        if (_browseMode == "artist-tracks" && !string.IsNullOrWhiteSpace(_listDrillDown))
        {
            return _repository.GetAllTracks().Where(t =>
                string.Equals(t.Artist, _listDrillDown, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.AlbumArtist, _listDrillDown, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (_browseMode == "genre-tracks" && !string.IsNullOrWhiteSpace(_listDrillDown))
            return _repository.GetTracksForGenre(_listDrillDown).ToList();
        if (_selectedAlbum is not null)
            return _repository.GetTracksForAlbum(_selectedAlbum.Album).ToList();
        if (TrackList.SelectedItem is LibraryAlbum album)
            return _repository.GetTracksForAlbum(album.Album).ToList();
        if (TrackList.SelectedItem is LibraryTrack track && !string.IsNullOrWhiteSpace(track.Album))
            return _repository.GetTracksForAlbum(track.Album).ToList();
        return [];
    }

    private void StartLyricsDownload(IReadOnlyList<LibraryTrack> tracks, string label)
    {
        var queued = _lyricsPrefetch.QueueDownload(tracks, LyricsQueueMode.Manual);
        if (queued == 0)
        {
            ViewTitleText.Text = $"Lyrics already cached for this {label}";
            return;
        }

        ViewTitleText.Text = $"Downloading lyrics for {label}: 0/{queued}…";
    }

    private async void EditAlbum_OnClick(object sender, RoutedEventArgs e)
    {
        var seed = ResolveCoverSeedTrack();
        if (seed is null && _browseMode == "album-tracks" && !string.IsNullOrWhiteSpace(_albumDrillDown))
            seed = _repository.GetTracksForAlbum(_albumDrillDown).FirstOrDefault();
        if (seed is null && _selectedAlbum is not null)
            seed = _repository.GetTracksForAlbum(_selectedAlbum.Album).FirstOrDefault();
        if (seed is null)
        {
            ViewTitleText.Text = "Select or open an album to edit";
            return;
        }

        var albumTracks = string.IsNullOrWhiteSpace(seed.Album)
            ? new List<LibraryTrack> { seed }
            : _repository.GetTracksForAlbum(seed.Album).ToList();
        if (albumTracks.Count == 0)
            albumTracks = [seed];

        var albumArtist = _selectedAlbum?.AlbumArtist
                          ?? albumTracks.Select(t => t.AlbumArtist).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a))
                          ?? seed.DisplayAlbumArtist;
        var albumName = seed.DisplayAlbum;

        var dlg = new AlbumEditorWindow(seed, albumTracks, albumArtist, albumName) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultAlbum is null)
            return;

        var newArtist = dlg.ResultAlbumArtist ?? "";
        var newAlbum = dlg.ResultAlbum;
        var updateCover = dlg.UpdateCover;
        var clearCover = dlg.ClearCover;
        var cover = dlg.ResultCover;

        EditAlbumButton.IsEnabled = false;
        ViewTitleText.Text = $"Updating album 0/{albumTracks.Count}…";

        (bool WasPlaying, TimeSpan Position)? resume = null;
        var current = _playback.CurrentTrack;
        if (current is not null &&
            albumTracks.Any(t => string.Equals(t.FilePath, current.FilePath, StringComparison.OrdinalIgnoreCase)))
        {
            var released = _playback.ReleaseCurrentFileIfMatches(current.FilePath);
            if (released is { } state)
                resume = (state.WasPlaying, state.Position);
        }

        var updated = 0;
        var errors = 0;
        await Task.Run(() =>
        {
            for (var i = 0; i < albumTracks.Count; i++)
            {
                var track = albumTracks[i];
                try
                {
                    byte[]? coverArt = track.CoverArt;
                    if (clearCover)
                        coverArt = null;
                    else if (updateCover && cover is { Length: > 0 })
                        coverArt = cover;

                    var edited = new LibraryTrack
                    {
                        Id = track.Id,
                        FilePath = track.FilePath,
                        Title = track.Title,
                        Artist = track.Artist,
                        AlbumArtist = newArtist,
                        Album = newAlbum,
                        TrackNumber = track.TrackNumber,
                        Year = track.Year,
                        Genre = track.Genre,
                        Duration = track.Duration,
                        Bitrate = track.Bitrate,
                        Format = track.Format,
                        DateAddedUtc = track.DateAddedUtc,
                        FileModifiedUtc = DateTime.UtcNow,
                        CoverArt = coverArt,
                        PlayCount = track.PlayCount,
                        LastPlayedUtc = track.LastPlayedUtc,
                        Rating = track.Rating,
                        ReplayGainTrackDb = track.ReplayGainTrackDb,
                        ReplayGainAlbumDb = track.ReplayGainAlbumDb,
                        ReplayGainTrackPeak = track.ReplayGainTrackPeak,
                        ReplayGainAlbumPeak = track.ReplayGainAlbumPeak,
                    };

                    try
                    {
                        AudioTagWriter.Write(edited);
                        if (clearCover)
                            CoverArtHelper.ClearCoverFromFile(track.AudioFilePath);
                    }
                    catch
                    {
                        Thread.Sleep(120);
                        AudioTagWriter.Write(edited);
                        if (clearCover)
                            CoverArtHelper.ClearCoverFromFile(track.AudioFilePath);
                    }

                        if (edited.Id > 0)
                        {
                            _repository.UpdateTrackMetadata(edited);
                            if (clearCover)
                                _repository.UpdateCoverArt(edited.Id, null);
                            else if (updateCover && cover is { Length: > 0 })
                                _repository.UpdateCoverArt(edited.Id, cover);
                        }

                    Interlocked.Increment(ref updated);
                }
                catch
                {
                    Interlocked.Increment(ref errors);
                }

                var done = i + 1;
                Dispatcher.Invoke(() => ViewTitleText.Text = $"Updating album {done}/{albumTracks.Count}…");
            }
        }).ConfigureAwait(true);

        if (resume is { } playState)
            _playback.ResumeAfterFileRelease(playState.WasPlaying, playState.Position);

        _albumDrillDown = newAlbum;
        _albumDrillDownMemory = newAlbum;
        if (_selectedAlbum is not null)
        {
            _selectedAlbum = new LibraryAlbum
            {
                Key = LibraryRepository.NormalizeAlbumKey(newAlbum),
                Album = newAlbum,
                AlbumArtist = string.IsNullOrWhiteSpace(newArtist) ? "Unknown Artist" : newArtist,
                TrackCount = albumTracks.Count,
                Year = _selectedAlbum.Year,
                CoverArt = clearCover ? null : cover ?? _selectedAlbum.CoverArt,
                IsSelected = true,
            };
        }

        EditAlbumButton.IsEnabled = true;
        if (_browseMode is "albums" or "album-tracks")
            _browseMode = "album-tracks";
        RefreshLibraryViews();
        RefreshQueueList();
        RefreshNowPlaying();
        ViewTitleText.Text = errors == 0
            ? $"Updated album on {updated} track(s)"
            : $"Updated {updated} track(s), {errors} failed";
    }

    private LibraryTrack? ResolveCoverSeedTrack()
    {
        if (TrackList.SelectedItem is LibraryTrack selected)
            return selected;

        if (_browseMode is "albums" or "home")
        {
            var album = _selectedAlbum;
            if (album is not null)
                return _repository.GetTracksForAlbum(album.Album).FirstOrDefault(t => t.CoverArt is { Length: > 0 })
                       ?? _repository.GetTracksForAlbum(album.Album).FirstOrDefault();
        }

        if (_browseMode == "album-tracks" && !string.IsNullOrWhiteSpace(_albumDrillDown))
            return _repository.GetTracksForAlbum(_albumDrillDown).FirstOrDefault(t => t.CoverArt is { Length: > 0 })
                   ?? _repository.GetTracksForAlbum(_albumDrillDown).FirstOrDefault();

        if (QueueList.SelectedItem is LibraryTrack queued)
            return queued;

        return _playback.CurrentTrack is { Id: > 0 } current
            ? _repository.GetTrackById(current.Id) ?? current
            : null;
    }

    private void ContextRate_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } || !int.TryParse(tag, out var rating))
            return;
        var track = ResolveSingleSelectedTrack();
        if (track is null)
            return;
        SetTrackRating(track.Id, rating);
    }

    private void EditTrackTags(LibraryTrack track)
    {
        var dlg = new TagEditorWindow(track) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null)
            return;

        try
        {
            AudioTagWriter.Write(dlg.Result);
            _repository.UpdateTrackMetadata(dlg.Result);
            _repository.SetRating(dlg.Result.Id, dlg.Result.Rating);
            RefreshLibraryViews();
            RefreshQueueList();
            ViewTitleText.Text = $"Updated tags: {dlg.Result.DisplayTitle}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not write tags:\n{ex.Message}", "Edit tags",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task EditBatchTagsAsync(IReadOnlyList<LibraryTrack> tracks)
    {
        var dlg = new BatchTagEditorWindow(tracks.Count) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Patch is null)
            return;

        var patch = dlg.Patch;
        var done = 0;
        var failed = 0;
        ViewTitleText.Text = $"Updating tags 0/{tracks.Count}…";

        (bool WasPlaying, TimeSpan Position)? resume = null;
        var current = _playback.CurrentTrack;

        await Task.Run(() =>
        {
            for (var i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                try
                {
                    Dispatcher.Invoke(() =>
                        ViewTitleText.Text = $"Updating tags {i + 1}/{tracks.Count}…");

                    var updated = patch.ApplyTo(track);
                    if (current is not null &&
                        string.Equals(current.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        var released = Dispatcher.Invoke(() => _playback.ReleaseCurrentFileIfMatches(track.FilePath));
                        if (released is { } state)
                            resume = (state.WasPlaying, state.Position);
                    }

                    if (patch.HasFileTagChanges)
                        AudioTagWriter.Write(updated);

                    Dispatcher.Invoke(() =>
                    {
                        _repository.UpdateTrackMetadata(updated);
                        if (patch.ApplyRating)
                            _repository.SetRating(updated.Id, updated.Rating);
                    });
                    done++;
                }
                catch
                {
                    failed++;
                }
            }
        }).ConfigureAwait(true);

        if (resume is { } playState)
            _playback.ResumeAfterFileRelease(playState.WasPlaying, playState.Position);

        RefreshLibraryViews();
        RefreshQueueList();
        RefreshNowPlaying();
        ViewTitleText.Text = failed == 0
            ? $"Updated tags on {done} track{(done == 1 ? "" : "s")}"
            : $"Updated {done} track{(done == 1 ? "" : "s")}; {failed} failed";
    }

    private void SetTrackRating(long trackId, int rating)
    {
        _repository.SetRating(trackId, rating);
        RefreshLibraryViews();
        RefreshQueueList();
        RefreshNowPlaying();
    }

    private void NewSmartPlaylist_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new SmartPlaylistEditorWindow(repository: _repository) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultRules is null || string.IsNullOrWhiteSpace(dlg.ResultName))
            return;

        var playlist = _repository.CreateSmartPlaylist(dlg.ResultName, dlg.ResultRules);
        SelectPlaylist(playlist);
    }

    private void EditSmartPlaylist_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetPlaylistFromNode(PlaylistNavTree.SelectedItem) is not LibraryPlaylist playlist || !playlist.IsSmart)
            return;

        var dlg = new SmartPlaylistEditorWindow(playlist.Name, playlist.Rules, isEdit: true, repository: _repository) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultRules is null || string.IsNullOrWhiteSpace(dlg.ResultName))
            return;

        _repository.UpdateSmartPlaylist(playlist.Id, dlg.ResultName, dlg.ResultRules);
        RefreshPlaylistNav();
        if (_selectedPlaylistId == playlist.Id)
            RefreshLibraryViews();
    }

    private void PlaylistNavContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        var playlist = GetPlaylistFromNode(PlaylistNavTree.SelectedItem);
        if (playlist is not null)
        {
            if (EditSmartPlaylistMenuItem is not null)
                EditSmartPlaylistMenuItem.Visibility = playlist.IsSmart ? Visibility.Visible : Visibility.Collapsed;
            if (MovePlaylistMenuItem is not null)
                MovePlaylistMenuItem.Visibility = Visibility.Visible;
            if (RenameFolderMenuItem is not null)
                RenameFolderMenuItem.Visibility = Visibility.Collapsed;
            if (DeleteFolderMenuItem is not null)
                DeleteFolderMenuItem.Visibility = Visibility.Collapsed;
            if (NewSubfolderMenuItem is not null)
                NewSubfolderMenuItem.Visibility = Visibility.Collapsed;
            if (FolderPlaylistSeparator is not null)
                FolderPlaylistSeparator.Visibility = Visibility.Visible;
        }
        else if (GetFolderFromNode(PlaylistNavTree.SelectedItem) is not null)
        {
            if (EditSmartPlaylistMenuItem is not null)
                EditSmartPlaylistMenuItem.Visibility = Visibility.Collapsed;
            if (MovePlaylistMenuItem is not null)
                MovePlaylistMenuItem.Visibility = Visibility.Collapsed;
            if (RenameFolderMenuItem is not null)
                RenameFolderMenuItem.Visibility = Visibility.Visible;
            if (DeleteFolderMenuItem is not null)
                DeleteFolderMenuItem.Visibility = Visibility.Visible;
            if (NewSubfolderMenuItem is not null)
                NewSubfolderMenuItem.Visibility = Visibility.Visible;
            if (FolderPlaylistSeparator is not null)
                FolderPlaylistSeparator.Visibility = Visibility.Collapsed;
        }
        else
        {
            if (EditSmartPlaylistMenuItem is not null)
                EditSmartPlaylistMenuItem.Visibility = Visibility.Collapsed;
            if (MovePlaylistMenuItem is not null)
                MovePlaylistMenuItem.Visibility = Visibility.Collapsed;
            if (RenameFolderMenuItem is not null)
                RenameFolderMenuItem.Visibility = Visibility.Collapsed;
            if (DeleteFolderMenuItem is not null)
                DeleteFolderMenuItem.Visibility = Visibility.Collapsed;
            if (NewSubfolderMenuItem is not null)
                NewSubfolderMenuItem.Visibility = Visibility.Collapsed;
            if (FolderPlaylistSeparator is not null)
                FolderPlaylistSeparator.Visibility = Visibility.Collapsed;
        }
    }

    private void SelectPlaylist(LibraryPlaylist playlist)
    {
        RefreshPlaylistNav();
        _browseMode = "playlist";
        _selectedPlaylistId = playlist.Id;
        _albumDrillDown = null;
        _selectedAlbum = null;
        _suppressNavEvents = true;
        BrowseList.SelectedItem = null;
        TrySelectTreeNode(FindPlaylistNode(_repository.GetPlaylistTree(), playlist.Id));
        _suppressNavEvents = false;
        RefreshLibraryViews();
    }

    private void NewPlaylistFolder_OnClick(object sender, RoutedEventArgs e) =>
        PromptCreatePlaylistFolder(parentId: null);

    private void NewPlaylistSubfolder_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetFolderFromNode(PlaylistNavTree.SelectedItem)?.FolderId is not long parentId)
            return;
        PromptCreatePlaylistFolder(parentId);
    }

    private void PromptCreatePlaylistFolder(long? parentId)
    {
        var dlg = new TextPromptWindow("New playlist folder", "Folder name:") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result))
            return;

        _repository.CreatePlaylistFolder(dlg.Result, parentId);
        RefreshPlaylistNav();
    }

    private void RenamePlaylistFolder_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetFolderFromNode(PlaylistNavTree.SelectedItem)?.FolderId is not long folderId)
            return;

        var folder = _repository.GetPlaylistFolders().FirstOrDefault(f => f.Id == folderId);
        if (folder is null)
            return;

        var dlg = new TextPromptWindow("Rename folder", "New name:", folder.Name) { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result))
            return;

        _repository.RenamePlaylistFolder(folderId, dlg.Result);
        RefreshPlaylistNav();
    }

    private void DeletePlaylistFolder_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetFolderFromNode(PlaylistNavTree.SelectedItem)?.FolderId is not long folderId)
            return;

        var folder = _repository.GetPlaylistFolders().FirstOrDefault(f => f.Id == folderId);
        if (folder is null)
            return;

        var confirm = MessageBox.Show(this,
            $"Delete folder “{folder.Name}”? Playlists inside will move to the top level.",
            "Delete folder",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        _repository.DeletePlaylistFolder(folderId);
        RefreshPlaylistNav();
    }

    private void MovePlaylistToFolder_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetPlaylistFromNode(PlaylistNavTree.SelectedItem)?.Id is not long playlistId)
            return;

        var folders = _repository.GetPlaylistFolders();
        var options = new List<string> { "(Top level)" };
        options.AddRange(folders.Select(f => f.Name));
        var dlg = new TextPromptWindow("Move playlist", "Folder name (or Top level):", options.First()) { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result))
            return;

        long? folderId = null;
        if (!dlg.Result.Equals("(Top level)", StringComparison.OrdinalIgnoreCase))
        {
            var folder = folders.FirstOrDefault(f =>
                f.Name.Equals(dlg.Result.Trim(), StringComparison.OrdinalIgnoreCase));
            if (folder is null)
            {
                folder = _repository.CreatePlaylistFolder(dlg.Result.Trim());
            }

            folderId = folder.Id;
        }

        _repository.MovePlaylistToFolder(playlistId, folderId);
        RefreshPlaylistNav();
    }

    private void NewPlaylist_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new TextPromptWindow("New playlist", "Playlist name:") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result))
            return;

        var playlist = _repository.CreatePlaylist(dlg.Result);
        SelectPlaylist(playlist);
    }

    private void RenamePlaylist_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetPlaylistFromNode(PlaylistNavTree.SelectedItem) is not LibraryPlaylist playlist)
            return;

        var dlg = new TextPromptWindow("Rename playlist", "New name:", playlist.Name) { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result))
            return;

        _repository.RenamePlaylist(playlist.Id, dlg.Result);
        RefreshPlaylistNav();
        if (_selectedPlaylistId == playlist.Id)
            RefreshLibraryViews();
    }

    private void DeletePlaylist_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetPlaylistFromNode(PlaylistNavTree.SelectedItem) is not LibraryPlaylist playlist)
            return;

        var confirm = MessageBox.Show(this,
            $"Delete playlist “{playlist.Name}”?",
            "Delete playlist",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        _repository.DeletePlaylist(playlist.Id);
        if (_selectedPlaylistId == playlist.Id)
        {
            _selectedPlaylistId = null;
            _albumDrillDown = null;
            _browseMode = "tracks";
            _suppressNavEvents = true;
            BrowseList.SelectedIndex = 0;
            _suppressNavEvents = false;
        }

        RefreshPlaylistNav();
        RefreshLibraryViews();
    }

    private void QueueList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var index = QueueList.SelectedIndex;
        if (index < 0 || index >= _playback.Queue.Count)
            return;

        // Play from here: drop tracks before the clicked item so the queue matches.
        var fromHere = _playback.Queue.Skip(index).ToList();
        _playback.SetQueue(fromHere, 0);
        _playback.Play();
    }

    private void QueueMoveUp_OnClick(object sender, RoutedEventArgs e)
    {
        var index = QueueList.SelectedIndex;
        if (index <= 0)
            return;
        _playback.MoveInQueue(index, index - 1);
        QueueList.SelectedIndex = index - 1;
    }

    private void QueueMoveDown_OnClick(object sender, RoutedEventArgs e)
    {
        var index = QueueList.SelectedIndex;
        if (index < 0 || index >= _playback.Queue.Count - 1)
            return;
        _playback.MoveInQueue(index, index + 1);
        QueueList.SelectedIndex = index + 1;
    }

    private void QueueRemove_OnClick(object sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedIndex < 0)
            return;
        _playback.RemoveFromQueue(QueueList.SelectedIndex);
    }

    private void QueueClear_OnClick(object sender, RoutedEventArgs e) =>
        _playback.ClearQueue(stopPlayback: false);

    private void PlayPause_OnClick(object sender, RoutedEventArgs e)
    {
        if (_playback.CurrentTrack is null)
        {
            PlayCurrentSelection(replaceQueue: true);
            return;
        }

        _playback.TogglePlayPause();
    }

    private void Previous_OnClick(object sender, RoutedEventArgs e) => _playback.Previous();
    private void Next_OnClick(object sender, RoutedEventArgs e) => _playback.Next();

    private void Shuffle_OnClick(object sender, RoutedEventArgs e)
    {
        var enabled = !_playback.ShuffleEnabled;
        _playback.SetShuffle(enabled);
        App.Settings.Shuffle = enabled;
        App.SaveSettings();
        UpdateShuffleRepeatChrome();
    }

    private void Repeat_OnClick(object sender, RoutedEventArgs e)
    {
        var mode = _playback.CycleRepeatMode();
        App.Settings.RepeatMode = mode.ToStorageValue();
        App.SaveSettings();
        UpdateShuffleRepeatChrome();
    }

    private void UpdateShuffleRepeatChrome()
    {
        if (ShuffleButton is not null)
            ShuffleButton.Opacity = _playback.ShuffleEnabled ? 1.0 : 0.45;

        if (ShuffleIcon is not null)
        {
            ShuffleIcon.Fill = _playback.ShuffleEnabled
                ? (FindResource("HubAccentBrush") as System.Windows.Media.Brush)
                : (FindResource("HubTextPrimaryBrush") as System.Windows.Media.Brush);
        }

        if (RepeatButton is null)
            return;

        var repeatOn = _playback.RepeatMode != PlaybackRepeatMode.Off;
        RepeatButton.Opacity = repeatOn ? 1.0 : 0.45;
        if (RepeatIcon is not null)
        {
            RepeatIcon.Fill = repeatOn
                ? (FindResource("HubAccentBrush") as System.Windows.Media.Brush)
                : (FindResource("HubTextPrimaryBrush") as System.Windows.Media.Brush);
        }

        if (RepeatOneBadge is not null)
            RepeatOneBadge.Visibility = _playback.RepeatMode == PlaybackRepeatMode.One
                ? Visibility.Visible
                : Visibility.Collapsed;

        RepeatButton.ToolTip = _playback.RepeatMode switch
        {
            PlaybackRepeatMode.One => "Repeat one",
            PlaybackRepeatMode.All => "Repeat all",
            _ => "Repeat off",
        };
    }

    private void NowPlayingRating_OnRatingChanged(object? sender, int rating)
    {
        if (_playback.CurrentTrack is null)
            return;
        SetTrackRating(_playback.CurrentTrack.Id, rating);
    }

    private void NowPlayingRate_OnClick(object sender, RoutedEventArgs e)
    {
        if (_playback.CurrentTrack is null)
            return;
        if (sender is not FrameworkElement { Tag: string tag } || !int.TryParse(tag, out var rating))
            return;
        SetTrackRating(_playback.CurrentTrack.Id, rating);
    }

    private void PositionSlider_OnPreviewMouseDown(object sender, MouseButtonEventArgs e) => _seeking = true;
    private void PositionSlider_OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _seeking = false;
        SeekFromSlider();
    }

    private void PositionSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_seeking)
            SeekFromSlider();
    }

    private void SeekFromSlider()
    {
        if (_playback.Duration <= TimeSpan.Zero)
            return;

        var target = TimeSpan.FromSeconds(PositionSlider.Value * _playback.Duration.TotalSeconds);
        _playback.Seek(target);
        RefreshNowPlaying();
    }

    private void PositionTimer_OnTick(object? sender, EventArgs e)
    {
        if (_seeking || _playback.CurrentTrack is null)
            return;

        RefreshNowPlaying();
        if (_playback.TryAdvanceAtCueEnd())
            return;

        _lastFm.OnPositionTick(
            _playback.CurrentTrack,
            _playback.Position,
            _playback.Duration,
            App.Settings);
    }

    private void UpdateDiscordPresence()
    {
        _discord.Update(_playback.CurrentTrack, _playback.IsPlaying);
    }

    private void RefreshNowPlaying()
    {
        var track = _playback.CurrentTrack;
        if (track is null)
        {
            NowPlayingTitle.Text = "Nothing playing";
            NowPlayingArtist.Text = "Select a track to begin";
            NowPlayingStats.Text = "";
            if (NowPlayingRating is not null)
            {
                NowPlayingRating.IsEnabled = false;
                NowPlayingRating.SyncRating(0);
            }
            SetPlayPauseIcons(playing: false);
            PositionSlider.Value = 0;
            ElapsedText.Text = "0:00";
            RemainingText.Text = "0:00";
            NowPlayingCover.Source = null;
            _tray.UpdateTooltip(null, null);
            UpdatePlayPauseChrome();
            _miniPlayer?.Refresh();
            return;
        }

        // Refresh stats from DB when possible.
        var live = track.Id > 0 ? _repository.GetTrackById(track.Id) ?? track : track;

        NowPlayingTitle.Text = live.DisplayTitle;
        NowPlayingArtist.Text = $"{live.DisplayArtist} — {live.DisplayAlbum}";
        NowPlayingStats.Text = live.PlayStatsLabel;
        if (NowPlayingRating is not null)
        {
            NowPlayingRating.IsEnabled = true;
            NowPlayingRating.SyncRating(Math.Clamp(live.Rating, 0, 5));
        }
        SetPlayPauseIcons(_playback.IsPlaying);
        UpdatePlayPauseChrome();
        UpdateShuffleRepeatChrome();
        _tray.UpdateTooltip(live.DisplayTitle, live.DisplayArtist);

        var duration = _playback.Duration;
        var position = _playback.Position;
        if (duration.TotalSeconds > 0 && !_seeking)
            PositionSlider.Value = position.TotalSeconds / duration.TotalSeconds;

        ElapsedText.Text = FormatTime(position);
        RemainingText.Text = duration > TimeSpan.Zero
            ? $"-{FormatTime(duration - position)}"
            : live.DurationLabel;

        NowPlayingCover.Source = CoverArtHelper.ToBitmap(live.CoverArt, 288, centerCropSquare: true);
        _miniPlayer?.Refresh();
        if (ArtistPanel?.Visibility == Visibility.Visible)
            _ = RefreshArtistPanelAsync(live.DisplayArtist);
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private void LibraryStats_OnClick(object sender, RoutedEventArgs e)
    {
        var stats = _repository.GetStatistics();
        var dlg = new StatisticsWindow(stats) { Owner = this };
        dlg.ShowDialog();
    }

    private void FindDuplicates_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new DuplicatesWindow(_repository) { Owner = this };
        dlg.LibraryChanged += (_, _) =>
        {
            PurgeMissingFromQueue();
            RefreshLibraryViews();
        };
        dlg.ShowDialog();
    }

    private void PurgeMissingFromQueue()
    {
        for (var i = _playback.Queue.Count - 1; i >= 0; i--)
        {
            try
            {
                if (!File.Exists(_playback.Queue[i].AudioFilePath))
                    _playback.RemoveFromQueue(i);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private void OrganizeFiles_OnClick(object sender, RoutedEventArgs e)
    {
        var tracks = ResolveSelectedTracks();
        if (tracks.Count == 0)
            tracks = _repository.GetAllTracks().ToList();

        if (tracks.Count == 0)
        {
            ViewTitleText.Text = "No tracks to organize";
            return;
        }

        var dlg = new OrganizePreviewWindow(_repository, tracks, _folderWatcher, _playback) { Owner = this };
        dlg.LibraryChanged += (_, _) => RefreshLibraryViews();
        dlg.ShowDialog();
    }

    private async void ScanReplayGain_OnClick(object sender, RoutedEventArgs e)
    {
        var tracks = _repository.GetAllTracks();
        if (tracks.Count == 0)
        {
            ViewTitleText.Text = "No tracks to scan";
            return;
        }

        ViewTitleText.Text = $"Scanning ReplayGain 0/{tracks.Count}…";
        var scanner = new ReplayGainScanner(_repository);
        var progress = new Progress<(int Done, int Total, string Path)>(p =>
            ViewTitleText.Text = $"Scanning ReplayGain {p.Done}/{p.Total}…");
        await scanner.ScanAsync(tracks, progress).ConfigureAwait(true);
        RefreshLibraryViews();
        ViewTitleText.Text = $"ReplayGain scan complete ({tracks.Count} tracks)";
    }

    private void RightPanelQueue_OnClick(object sender, RoutedEventArgs e)
    {
        if (RightPanelQueueButton is not null)
            RightPanelQueueButton.IsChecked = true;
        if (RightPanelArtistButton is not null)
            RightPanelArtistButton.IsChecked = false;
        if (QueuePanel is not null)
            QueuePanel.Visibility = Visibility.Visible;
        if (ArtistPanel is not null)
            ArtistPanel.Visibility = Visibility.Collapsed;
    }

    private void RightPanelArtist_OnClick(object sender, RoutedEventArgs e)
    {
        if (RightPanelQueueButton is not null)
            RightPanelQueueButton.IsChecked = false;
        if (RightPanelArtistButton is not null)
            RightPanelArtistButton.IsChecked = true;
        if (QueuePanel is not null)
            QueuePanel.Visibility = Visibility.Collapsed;
        if (ArtistPanel is not null)
            ArtistPanel.Visibility = Visibility.Visible;
        var artist = _playback.CurrentTrack?.DisplayArtist ?? "";
        _ = RefreshArtistPanelAsync(artist);
    }

    private async Task RefreshArtistPanelAsync(string artist)
    {
        if (ArtistPanelName is null || ArtistPanelStats is null || ArtistPanelBio is null)
            return;

        if (string.IsNullOrWhiteSpace(artist))
        {
            ArtistPanelName.Text = "No artist";
            ArtistPanelStats.Text = "";
            ArtistPanelBio.Text = "Start playing a track to see artist details.";
            if (ArtistPanelImage is not null)
                ArtistPanelImage.Source = null;
            if (ArtistPanelSource is not null)
                ArtistPanelSource.Text = "";
            return;
        }

        var stats = _repository.GetArtistStats(artist);
        ArtistPanelName.Text = artist;
        ArtistPanelStats.Text =
            $"{stats.TrackCount} tracks · {stats.AlbumCount} albums · {stats.PlayCount} plays";
        ArtistPanelBio.Text = "Loading bio…";
        if (ArtistPanelSource is not null)
            ArtistPanelSource.Text = "";

        var info = await LastFmArtistInfoService.GetArtistInfoAsync(artist).ConfigureAwait(true);
        ArtistPanelBio.Text = string.IsNullOrWhiteSpace(info.Bio)
            ? "No biography available."
            : info.Bio;
        if (ArtistPanelSource is not null)
            ArtistPanelSource.Text = info.Source;
        if (ArtistPanelImage is not null && !string.IsNullOrWhiteSpace(info.ImageUrl))
        {
            try
            {
                var bytes = await new HttpClient().GetByteArrayAsync(info.ImageUrl).ConfigureAwait(true);
                ArtistPanelImage.Source = CoverArtHelper.ToBitmap(bytes, 120, centerCropSquare: true);
            }
            catch
            {
                ArtistPanelImage.Source = null;
            }
        }
    }

    private void ContextMarkInboxDone_OnClick(object sender, RoutedEventArgs e)
    {
        var tracks = ResolveSelectedTracks();
        if (tracks.Count == 0)
            return;
        _repository.SetReviewStatus(tracks.Select(t => t.Id), "done");
        RefreshLibraryViews();
        ViewTitleText.Text = $"Marked {tracks.Count} track(s) done";
    }

    private async void ContextAcoustId_OnClick(object sender, RoutedEventArgs e)
    {
        var track = ResolveSelectedTracks().FirstOrDefault();
        if (track is null)
            return;
        if (!_acoustId.IsConfigured)
        {
            MessageBox.Show(this, "Set AcoustID API key and fpcalc path in Settings first.", "AcoustID",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ViewTitleText.Text = $"Identifying “{track.DisplayTitle}” with AcoustID…";
        var result = await _acoustId.IdentifyTrackAsync(_repository, track).ConfigureAwait(true);
        ViewTitleText.Text = result.Message;
        if (result.Success)
            RefreshLibraryViews();
    }

    public void ShowUpdateAvailable(UpdateCheckResult result)
    {
        _pendingUpdate = result;
        UpdateTitle.Text = $"Version {result.LatestVersion} is available";
        UpdateBody.Text = $"You are on {App.VersionDisplay}. Run the installer to update — your library in {AppPaths.DataDirectory} is kept.";
        UpdateCard.Visibility = Visibility.Visible;
    }

    private void UpdateLater_OnClick(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate?.LatestVersion is { } v)
            App.Settings.DismissedUpdateVersion = v;
        App.SaveSettings();
        UpdateCard.Visibility = Visibility.Collapsed;
    }

    private void UpdateDownload_OnClick(object sender, RoutedEventArgs e) =>
        UpdateCheckService.OpenUpdateDownload(_pendingUpdate?.InstallerDownloadUrl);

    private void Settings_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_lyricsPrefetch, () => _repository.GetAllTracks()) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            HubTheme.ApplyFromSettings();
            _playback.SetVolume(App.Settings.DefaultVolume);
            _playback.ReloadOutputSettings();
            SyncSpeedComboFromSettings();
            _downloaderBridge.Apply(App.Settings);
            _discord.ApplySettings(App.Settings);
            _scriptHooks.Enabled = App.Settings.ScriptHooksEnabled;
            if (_scriptHooks.Enabled)
                _scriptHooks.EnsureScriptsFolder();
            _lyricsPrefetch.Enabled = App.Settings.AutoDownloadLyrics;
            UpdateDiscordPresence();
            ApplyFolderWatcher();
            ApplyDownloaderSidebarLayout();
            RefreshDownloaderStatus();
            OnTrayPreferenceChanged();
            if (dlg.RescanRequested)
                _ = ScanLibraryAsync();
        }
    }
}
