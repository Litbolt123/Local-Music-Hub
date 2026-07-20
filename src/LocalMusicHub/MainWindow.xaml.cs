using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LocalMusicHub.Data;
using LocalMusicHub.Models;
using LocalMusicHub.Services;
using MessageBox = System.Windows.MessageBox;

namespace LocalMusicHub;

public partial class MainWindow
{
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
    private bool _positionScrubbing;
    private bool _suppressPositionSlider;
    private bool _positionSeekCommitted;
    private bool _positionSeekPending;
    private bool _forceClose;
    private bool _positionRenderActive;
    private EventHandler? _positionRenderingHandler;
    private TimeSpan _positionAnchor;
    private TimeSpan _positionAnchorDuration;
    private DateTime _positionAnchorUtc;
    private double _smoothedSliderFraction;
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
        StartupProfiler.Mark("mainwindow.ctor.enter");
        HubTheme.Ensure(this);
        StartupProfiler.Mark("mainwindow.theme_ensured");
        InitializeComponent();
        StartupProfiler.Mark("mainwindow.init_component");
        _startMinimizedToTray = AutoStartService.ArgsRequestTray(Environment.GetCommandLineArgs());
        _positionTimer.Tick += PositionTimer_OnTick;
        _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _searchDebounceTimer.Tick += SearchDebounceTimer_OnTick;
        SetupImportRequestWatcher();
        StartupProfiler.Mark("mainwindow.ctor.done");
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

        Tray.ShowMainWindow();
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
            if (Playback.CurrentTrack is not null)
            {
                Playback.TogglePlayPause();
                RefreshNowPlaying();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.MediaPlayPause)
        {
            if (Playback.CurrentTrack is not null)
            {
                Playback.TogglePlayPause();
                RefreshNowPlaying();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.MediaNextTrack)
        {
            Playback.Next();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.MediaPreviousTrack)
        {
            Playback.Previous();
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
        StartupProfiler.Mark("mainwindow.loaded");
        _suppressVolumeSlider = true;
        if (VolumeSlider is not null)
            VolumeSlider.Value = App.Settings.DefaultVolume;
        _suppressVolumeSlider = false;
        SyncSpeedComboFromSettings();
        ApplySidebarLayoutFromSettings();
        UpdateMuteChrome();
        UpdateOpenButtonVisibility();
        HookPositionSliderThumb();
        _positionTimer.Start();

        BeginInitialLibraryLoad();
        Dispatcher.BeginInvoke(EnsurePlaybackConfigured, DispatcherPriority.Background);
        Dispatcher.BeginInvoke(DeferSecondaryInitialization, DispatcherPriority.ApplicationIdle);

        Tray.ShowTrayIcon();

        if (_startMinimizedToTray || (App.Settings.StartWithWindows && AutoStartService.ArgsRequestTray(Environment.GetCommandLineArgs())))
        {
            Hide();
            TryApplyCachedUpdate();
            StartupProfiler.Mark("mainwindow.loaded.done");
            return;
        }

        TryApplyCachedUpdate();
        StartupProfiler.Mark("mainwindow.loaded.done");
    }

    private void MainWindow_OnContentRendered(object? sender, EventArgs e)
    {
        StartupProfiler.Mark("ui.first_paint");
    }

    private void DeferSecondaryInitialization()
    {
        StartupProfiler.Mark("defer.secondary.start");

        ScriptHooks.Enabled = App.Settings.ScriptHooksEnabled;
        if (ScriptHooks.Enabled)
            ScriptHooks.EnsureScriptsFolder();

        _sleepTimer = new SleepTimerService(
            vol =>
            {
                _suppressVolumeSlider = true;
                Playback.SetVolume(vol);
                if (VolumeSlider is not null)
                    VolumeSlider.Value = vol;
                _suppressVolumeSlider = false;
                UpdateMuteChrome();
            },
            () =>
            {
                Playback.Pause();
                RefreshNowPlaying();
            });
        _sleepTimer.Changed += (_, _) => Dispatcher.Invoke(UpdateSleepTimerChrome);
        _mediaKeys = new GlobalMediaKeyService(this);
        _mediaKeys.PlayPauseRequested += () => Dispatcher.Invoke(() =>
        {
            if (Playback.CurrentTrack is null)
                return;
            Playback.TogglePlayPause();
            RefreshNowPlaying();
        });
        _mediaKeys.NextRequested += () => Dispatcher.Invoke(() => Playback.Next());
        _mediaKeys.PreviousRequested += () => Dispatcher.Invoke(() => Playback.Previous());
        _mediaKeys.StopRequested += () => Dispatcher.Invoke(() =>
        {
            Playback.Pause();
            RefreshNowPlaying();
        });
        _mediaKeys.MuteRequested += () => Dispatcher.Invoke(() => Mute_OnClick(this, new RoutedEventArgs()));
        _mediaKeys.VolumeUpRequested += () => Dispatcher.Invoke(() => AdjustVolume(+0.05));
        _mediaKeys.VolumeDownRequested += () => Dispatcher.Invoke(() => AdjustVolume(-0.05));
        ApplyDownloaderSidebarLayout();
        RefreshDownloaderStatus();
        ApplyLibraryIngestHost();

        var settings = App.Settings;
        _ = Task.Run(() =>
        {
            StartupProfiler.Mark("defer.secondary.bg.start");
            var foldersBefore = string.Join('|', settings.LibraryFolders);
            DownloaderBridge.Apply(settings);
            Discord.ApplySettings(settings);
            ApplyFolderWatcher();
            if (settings.AutoDownloadLyrics)
                EnsureLyricsPrefetchOnBackground(settings.AutoDownloadLyrics);
            StartupProfiler.Mark("defer.secondary.import.start");
            RunStartupImportOnBackground();
            StartupProfiler.Mark("defer.secondary.import.done");
            StartupProfiler.Mark("defer.secondary.bg.done");

            var foldersAfter = string.Join('|', settings.LibraryFolders);
            if (!string.Equals(foldersBefore, foldersAfter, StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.BeginInvoke(() =>
                {
                    App.SaveSettings();
                    StartupProfiler.Mark("defer.secondary.done");
                }, DispatcherPriority.ApplicationIdle);
            }
            else
            {
                Dispatcher.BeginInvoke(() => StartupProfiler.Mark("defer.secondary.done"),
                    DispatcherPriority.ApplicationIdle);
            }
        });
    }

    private void HookFolderWatcherWhenReady()
    {
        if (_folderWatcherHooked)
            return;

        _folderWatcherHooked = true;
        FolderWatcher.LibraryChanged += OnFolderWatcherLibraryChanged;
    }

    private void OnFolderWatcherLibraryChanged(object? sender, EventArgs e)
    {
        if (!_libraryUiReady)
            return;

        Dispatcher.BeginInvoke(RefreshLibraryViews, DispatcherPriority.Background);
    }

    private void RunStartupImportOnBackground()
    {
        if (!string.IsNullOrWhiteSpace(App.PendingImportPath))
        {
            if (App.PendingImportFolder)
                ImportAudioFolder(App.PendingImportPath, fromExternalRequest: true);
            else
                ImportAudioFile(App.PendingImportPath, fromExternalRequest: true);
            return;
        }

        if (!LibraryImportRequestService.TryReadPending(out var request) || request is null)
            return;

        LibraryImportRequestService.ClearPending();
        if (request.ImportFolder)
            ImportAudioFolder(request.Path, fromExternalRequest: true);
        else
            ImportAudioFile(request.Path, fromExternalRequest: true);
    }

    private void RefreshLibraryViewsSafe()
    {
        if (Dispatcher.CheckAccess())
            RefreshLibraryViews();
        else
            Dispatcher.BeginInvoke(RefreshLibraryViews);
    }

    private void BeginInitialLibraryLoad()
    {
        if (LibraryStatsText is not null)
            LibraryStatsText.Text = "Loading library…";

        StartupProfiler.Mark("library.load.scheduled");
        var browseMode = _browseMode;
        var ui = Dispatcher;
        _ = Task.Run(() =>
        {
            StartupProfiler.Mark("library.bg_task.start");
            var t0 = StartupProfiler.NowMs();
            var trackCount = Repository.TrackCount();
            StartupProfiler.MarkAfter("library.track_count", t0);

            t0 = StartupProfiler.NowMs();
            var playlistTree = Repository.GetPlaylistTree(includeCoverArt: false);
            StartupProfiler.MarkAfter("library.playlist_tree", t0);

            IReadOnlyList<LibraryAlbum>? recent = null;
            IReadOnlyList<LibraryAlbum>? added = null;
            if (browseMode == "home")
            {
                t0 = StartupProfiler.NowMs();
                recent = Repository.GetRecentlyPlayedAlbums(16);
                StartupProfiler.MarkAfter("library.recent_albums", t0);

                t0 = StartupProfiler.NowMs();
                added = Repository.GetRecentlyAddedAlbums(16);
                StartupProfiler.MarkAfter("library.added_albums", t0);

                t0 = StartupProfiler.NowMs();
                CoverArtHelper.WarmAlbumThumbnails(recent);
                CoverArtHelper.WarmAlbumThumbnails(added);
                StartupProfiler.MarkAfter("library.warm_thumbnails", t0);
            }

            StartupProfiler.Mark("library.bg_task.done");
            return (trackCount, playlistTree, recent, added);
        }).ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully)
            {
                ui.BeginInvoke(() => StartupProfiler.Finish("startup.aborted"));
                return;
            }

            ui.BeginInvoke(
                () => ApplyInitialLibraryLoad(t.Result),
                DispatcherPriority.Loaded);
        }, TaskScheduler.Default);
    }

    private void ApplyInitialLibraryLoad(
        (int trackCount, IReadOnlyList<PlaylistTreeNode> playlistTree, IReadOnlyList<LibraryAlbum>? recent, IReadOnlyList<LibraryAlbum>? added) result)
    {
        if (!IsLoaded)
            return;

        var (trackCount, playlistTree, recent, added) = result;
        var t0 = StartupProfiler.NowMs();
        RefreshPlaylistNav(playlistTree);
        StartupProfiler.MarkAfter("library.ui.playlist_nav", t0);

        if (_browseMode == "home" && recent is not null && added is not null)
        {
            t0 = StartupProfiler.NowMs();
            ViewTitleText.Text = "Home";
            if (SearchBox is not null)
                SearchBox.Visibility = Visibility.Collapsed;
            ShowHome(recent, added);
            StartupProfiler.MarkAfter("library.ui.show_home", t0);
            if (LibraryStatsText is not null)
                LibraryStatsText.Text = $"{trackCount} tracks in library";
            UpdateOpenButtonVisibility();
            UpdateQuickFilterChrome();
            RefreshNowPlaying();
        }
        else
        {
            t0 = StartupProfiler.NowMs();
            RefreshLibraryViews();
            StartupProfiler.MarkAfter("library.ui.refresh_views", t0);
            if (LibraryStatsText is not null)
                LibraryStatsText.Text = $"{trackCount} tracks in library";
        }

        if (trackCount == 0)
            _ = ScanLibraryAsync();

        _libraryUiReady = true;
        FolderWatcher.SuppressEvents = false;
        HookFolderWatcherWhenReady();

        StartupProfiler.Finish($"startup.ready ({trackCount} tracks, {playlistTree.Count} playlist nodes)");
    }

    public void ApplyStartupUpdate(UpdateCheckResult result, bool showTrayBalloon)
    {
        ShowUpdateAvailable(result);
        if (!showTrayBalloon)
            return;

        var url = !string.IsNullOrWhiteSpace(result.InstallerDownloadUrl)
            ? result.InstallerDownloadUrl!
            : UpdateCheckService.ReleasesPageUrl;
        Tray.ShowUpdateAvailableBalloon(result.LatestVersion ?? "?", url);
    }

    private void TryApplyCachedUpdate()
    {
        if (!UpdateAvailabilityCache.HasPending)
            return;

        var version = UpdateAvailabilityCache.PendingVersion;
        if (string.IsNullOrWhiteSpace(version))
            return;

        if (string.Equals(App.Settings.DismissedUpdateVersion, version, StringComparison.OrdinalIgnoreCase))
            return;

        ShowUpdateAvailable(UpdateAvailabilityCache.ToResult());
    }

    public void RequestForceClose() => _forceClose = true;

    public void OnTrayPreferenceChanged()
    {
        Tray.ShowTrayIcon();
        if (!App.Settings.MinimizeToTray && !App.Settings.StartWithWindows && !IsVisible)
            Tray.ShowMainWindow();
    }

    private void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_forceClose && (App.Settings.MinimizeToTray || App.Settings.StartWithWindows))
        {
            e.Cancel = true;
            Tray.MinimizeToTray();
            return;
        }

        _scanCts?.Cancel();
        _positionTimer.Stop();
        SetPositionRenderActive(false);
        _miniPlayer?.CloseFromOwner();
        _downloadPollCts?.Cancel();
        _downloadPollCts?.Dispose();
        _importRequestWatcher?.Dispose();
        DisposeLazyServices();
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
        var trackCount = Repository.GetAllTracks().Count;
        var dlg = new LibraryToolsWindow(
            trackCount,
            onStats: () => LibraryStats_OnClick(this, new RoutedEventArgs()),
            onDuplicates: () => FindDuplicates_OnClick(this, new RoutedEventArgs()),
            onOrganize: () => OrganizeFiles_OnClick(this, new RoutedEventArgs()),
            onReplayGain: () => ScanReplayGain_OnClick(this, new RoutedEventArgs()),
            onCleanDead: () => CleanDead_OnClick(this, new RoutedEventArgs()),
            onScanLibrary: () => ScanLibrary_OnClick(this, new RoutedEventArgs()),
            onScanFolders: () => ScanFolders_OnClick(this, new RoutedEventArgs()))
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
        FolderWatcher.SuppressEvents = true;
        FolderWatcher.Apply(roots, App.Settings.WatchLibraryFolders);
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

        var status = DownloaderBridge.GetStatus();
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

        DownloaderBridge.Apply(App.Settings);
        var link = DownloaderBridge.GetStatus();
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
            var health = await DownloaderApi.HealthAsync(port).ConfigureAwait(true);
            if (!health.Ok)
            {
                SetDownloaderJobStatus(health.Error ?? "YouTube Downloader is offline.");
                return;
            }

            var check = await DownloaderApi.CheckAsync(port, token, url!).ConfigureAwait(true);
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

            var download = await DownloaderApi.DownloadAsync(port, token, url!, forceRedownload).ConfigureAwait(true);
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

            Repository.UpsertTrack(track);
            ScriptHooks.OnImport(path);
            _lyricsPrefetchService?.Enqueue(track);
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
            {
                Dispatcher.BeginInvoke(() =>
                    ViewTitleText.Text = "Import failed: album folder not found");
            }
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
                    Repository.UpsertTrack(track);
                    ScriptHooks.OnImport(path);
                    _lyricsPrefetchService?.Enqueue(track);
                    imported++;
                }
                catch
                {
                    /* skip bad files */
                }
            }

            RefreshLibraryViewsSafe();

            if (fromExternalRequest)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ViewTitleText.Text = imported > 0
                        ? $"Imported album: {imported} track{(imported == 1 ? "" : "s")}"
                        : "No tracks imported from album folder";
                    if (IsVisible)
                        Activate();
                });
            }
            else
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ViewTitleText.Text = imported > 0
                        ? $"Added album: {imported} track{(imported == 1 ? "" : "s")}"
                        : "No tracks added from album folder";
                });
            }
        }
        catch (Exception ex)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (fromExternalRequest)
                    ViewTitleText.Text = $"Album import failed: {ex.Message}";
                else
                    SetDownloaderJobStatus($"Error importing album: {ex.Message}");
            });
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

    private async void ScanFolders_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new ScanFoldersWindow(App.Settings.LibraryFolders) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        await ScanLibraryAsync(dlg.SelectedFolders, folderScan: true);
    }

    private void CleanDead_OnClick(object sender, RoutedEventArgs e)
    {
        var removed = Repository.RemoveDeadEntries();
        RefreshLibraryViews();
        RefreshPlaylistNav();
        ViewTitleText.Text = removed == 0
            ? "No missing files found"
            : $"Removed {removed} missing file(s)";
    }

    private async Task ScanLibraryAsync(IReadOnlyList<string>? roots = null, bool folderScan = false)
    {
        if (_scanInProgress)
            return;

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        var scanRoots = roots?
            .Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (scanRoots is null or { Count: 0 })
        {
            folderScan = false;
            scanRoots = App.Settings.LibraryFolders.Where(Directory.Exists).Distinct().ToList();
            if (scanRoots.Count == 0)
                scanRoots.Add(AppPaths.DefaultMusicFolder);
        }

        _scanInProgress = true;
        BrowseList.IsEnabled = false;
        PlaylistNavTree.IsEnabled = false;
        ViewTitleText.Text = folderScan
            ? $"Scanning {scanRoots.Count} folder{(scanRoots.Count == 1 ? "" : "s")}…"
            : "Scanning…";
        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ViewTitleText.Text = p.Total > 0
                    ? (folderScan
                        ? $"Scanning folders… {p.Done}/{p.Total}"
                        : $"Scanning… {p.Done}/{p.Total}")
                    : (folderScan ? "Scanning folders…" : "Scanning library…");
            });

            var result = folderScan
                ? await Scanner.ScanFoldersAsync(scanRoots, progress, token).ConfigureAwait(true)
                : await Scanner.ScanAsync(scanRoots, progress, token).ConfigureAwait(true);

            RefreshLibraryViews();
            RefreshPlaylistNav();
            ViewTitleText.Text = folderScan
                ? $"Folder scan complete — {result.Indexed} tracks indexed"
                : $"Scan complete — {result.Indexed} tracks indexed";
            ScriptHooks.OnScanComplete(result.Indexed);
            if (App.Settings.AutoDownloadLyrics)
            {
                _ = Task.Run(() =>
                {
                    var prefetch = EnsureLyricsPrefetch(true);
                    prefetch.EnqueueMany(Repository.GetAllTracks());
                });
            }
        }
        catch (OperationCanceledException)
        {
            ViewTitleText.Text = "Scan cancelled";
        }
        catch (Exception ex)
        {
            ViewTitleText.Text = "Library scan failed";
            MessageBox.Show(this, ex.Message, "Scan failed", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            Repository.RecordPlay(track.Id);
            LastFm.OnTrackStarted(track);
            ScriptHooks.OnTrackStarted(track);
            _lyricsPrefetchService?.Enqueue(track);
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

        var tracks = Repository.GetPlaylistTracks(playlist.Id);
        if (tracks.Count == 0)
            return;

        Playback.SetQueue(tracks, 0);
        Playback.Play();
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

    private void RefreshPlaylistNav(IReadOnlyList<PlaylistTreeNode>? tree = null)
    {
        var selectedId = _selectedPlaylistId;
        tree ??= Repository.GetPlaylistTree(includeCoverArt: false);
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
                    ShowAlbumGrid(Repository.GetAlbums());
                    break;
                case "artists":
                    ViewTitleText.Text = "Artists";
                    if (SearchBox is not null)
                    {
                        SearchBox.Visibility = Visibility.Visible;
                        SearchBox.ToolTip = "Filter artists by name";
                    }

                    ApplyListTemplate("artists");
                    var artists = Repository.GetArtists();
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
                    var genres = Repository.GetGenres();
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
                        ? Repository.GetAllTracks().Where(t =>
                            string.Equals(t.Artist, _listDrillDown, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(t.AlbumArtist, _listDrillDown, StringComparison.OrdinalIgnoreCase)).ToList()
                        : Repository.GetTracksForGenre(_listDrillDown ?? "").ToList();

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
                    TrackList.ItemsSource = Playback.Queue.ToList();
                    break;
                case "playlist":
                    if (SearchBox is not null)
                        SearchBox.Visibility = Visibility.Visible;
                    ApplyListTemplate("tracks");
                    if (_selectedPlaylistId is long playlistId)
                    {
                        var playlist = Repository.GetPlaylists().FirstOrDefault(p => p.Id == playlistId);
                        ViewTitleText.Text = playlist is null ? "Playlist" : playlist.Name;
                        var playlistTracks = Repository.GetPlaylistTracks(playlistId).ToList();
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
                        var albumTracks = Repository.GetTracksForAlbum(_albumDrillDown).ToList();
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
                    var inboxTracks = Repository.GetInboxTracks(search);
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
                    var allTracks = Repository.QueryTracks(search, BuildQuickFilterRules());
                    TrackList.ItemsSource = allTracks;
                    if (allTracks.Count == 0)
                    {
                        UpdateEmptyHint(true, string.IsNullOrWhiteSpace(search) && _quickFilters.Count == 0
                            ? "No tracks in your library yet.\nScan a music folder to get started."
                            : "No tracks match your search or filters.");
                    }

                    break;
            }

            LibraryStatsText.Text = $"{Repository.TrackCount()} tracks in library";
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
        _ = Task.Run(() =>
        {
            CoverArtHelper.WarmAlbumThumbnails(albums);
        });
        UpdateAlbumCardPauseIndicators();
    }

    private void ShowHome(
        IReadOnlyList<LibraryAlbum>? recent = null,
        IReadOnlyList<LibraryAlbum>? added = null)
    {
        TrackList.Visibility = Visibility.Collapsed;
        AlbumGridScroller.Visibility = Visibility.Collapsed;
        if (HomeScroller is null)
            return;

        HomeScroller.Visibility = Visibility.Visible;
        recent ??= Repository.GetRecentlyPlayedAlbums(16);
        added ??= Repository.GetRecentlyAddedAlbums(16);

        CoverArtHelper.WarmAlbumThumbnails(recent);
        CoverArtHelper.WarmAlbumThumbnails(added);

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

        if (_playbackService is null)
        {
            QueueList.ItemsSource = null;
            if (QueueStatsText is not null)
                QueueStatsText.Text = "Queue is empty";
            return;
        }

        var queue = Playback.Queue.ToList();
        QueueList.ItemsSource = queue;
        if (Playback.QueueIndex >= 0 && Playback.QueueIndex < queue.Count)
            QueueList.SelectedIndex = Playback.QueueIndex;

        QueueStatsText.Text = queue.Count == 0
            ? "Queue is empty"
            : $"{queue.Count} in queue · now #{Math.Max(1, Playback.QueueIndex + 1)}";

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
        if (IsAlbumNowPlaying(album.Album) && Playback.IsPlaying)
        {
            Playback.Pause();
            UpdatePlayPauseChrome();
            e.Handled = true;
            return;
        }

        if (IsAlbumNowPlaying(album.Album) && Playback.IsPaused)
        {
            Playback.Play();
            UpdatePlayPauseChrome();
            e.Handled = true;
            return;
        }

        PlayAlbum(album);
        e.Handled = true;
    }

    private void PlayAlbum(LibraryAlbum album)
    {
        var tracks = Repository.GetTracksForAlbum(album.Album);
        if (tracks.Count == 0)
            return;
        Playback.SetQueue(tracks, 0);
        Playback.Play();
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
            Playback.TogglePlayPause();
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
            Playback.TogglePlayPause();
            UpdatePlayPauseChrome();
            return;
        }

        PlayCurrentSelection(replaceQueue: true);
        UpdatePlayPauseChrome();
    }

    private bool IsAlbumNowPlaying(string albumTitle)
    {
        var current = Playback.CurrentTrack;
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
        if (Playback.CurrentTrack is null || string.IsNullOrWhiteSpace(_listDrillDown))
            return false;

        if (_browseMode == "artist-tracks")
        {
            var t = Playback.CurrentTrack;
            return string.Equals(t.Artist, _listDrillDown, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(t.AlbumArtist, _listDrillDown, StringComparison.OrdinalIgnoreCase);
        }

        if (_browseMode == "genre-tracks")
        {
            return GenreNormalizer.ContainsGenre(Playback.CurrentTrack.Genre, _listDrillDown);
        }

        return false;
    }

    private bool IsSelectionContextNowPlaying()
    {
        if (Playback.CurrentTrack is null)
            return false;

        if (_browseMode == "album-tracks")
            return IsViewingAlbumNowPlaying();

        if (_browseMode is "artist-tracks" or "genre-tracks")
            return IsViewingListDrillDownNowPlaying();

        if ((_browseMode is "albums" or "home") && _selectedAlbum is not null)
            return IsAlbumNowPlaying(_selectedAlbum.Album);

        // Track / playlist / artist lists: pause if the current track is in this view's playable set.
        var tracks = ResolveTracksForPlayback();
        var current = Playback.CurrentTrack;
        return tracks.Any(t => t.Id == current.Id ||
            string.Equals(t.FilePath, current.FilePath, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdatePlayPauseChrome()
    {
        SetPlayPauseIcons(Playback.IsPlaying);
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

        var showPause = IsSelectionContextNowPlaying() && Playback.IsPlaying;

        if (AlbumPlayPausePlayIcon is not null)
            AlbumPlayPausePlayIcon.Visibility = showPause ? Visibility.Collapsed : Visibility.Visible;
        if (AlbumPlayPausePauseIcon is not null)
            AlbumPlayPausePauseIcon.Visibility = showPause ? Visibility.Visible : Visibility.Collapsed;

        AlbumPlayPauseButton.ToolTip = showPause
            ? "Pause"
            : _browseMode switch
            {
                "artist-tracks" or "genre-tracks" => "Play",
                "playlist" => "Play playlist",
                _ => "Play album"
            };
    }

    private void UpdateToolbarPlayPauseButton()
    {
        if (PlaySelectionButton is null)
            return;

        if (IsSelectionContextNowPlaying() && Playback.IsPlaying)
            PlaySelectionButton.Content = "Pause";
        else
            PlaySelectionButton.Content = "Play";
    }

    private void UpdateAlbumCardPauseIndicators()
    {
        var current = Playback.CurrentTrack;
        var playingKey = current is null || !Playback.IsPlaying
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
        Playback.AddToQueue(tracks);
    }

    private void PlayNext_OnClick(object sender, RoutedEventArgs e)
    {
        var tracks = ResolveSelectedTracks();
        if (tracks.Count == 0)
            return;
        Playback.InsertAfterCurrent(tracks);
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

            Playback.SetQueue(tracks, start);
            Playback.Play();
        }
    }

    private List<LibraryTrack> ResolveTracksForPlayback()
    {
        if (_browseMode == "queue")
            return Playback.Queue.ToList();

        if (_browseMode is "albums" or "home")
        {
            var album = _selectedAlbum;
            if (album is not null)
                return Repository.GetTracksForAlbum(album.Album).ToList();
            return [];
        }

        if (_browseMode == "album-tracks" && !string.IsNullOrWhiteSpace(_albumDrillDown))
            return Repository.GetTracksForAlbum(_albumDrillDown).ToList();

        if (_browseMode == "artist-tracks" && !string.IsNullOrWhiteSpace(_listDrillDown))
        {
            return Repository.GetAllTracks().Where(t =>
                string.Equals(t.Artist, _listDrillDown, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.AlbumArtist, _listDrillDown, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (_browseMode == "genre-tracks" && !string.IsNullOrWhiteSpace(_listDrillDown))
            return Repository.GetTracksForGenre(_listDrillDown).ToList();

        if (TrackList.SelectedItem is LibraryAlbum selectedAlbum)
            return Repository.GetTracksForAlbum(selectedAlbum.Album).ToList();

        if (TrackList.SelectedItem is LibraryArtist artist)
            return Repository.GetAllTracks().Where(t =>
                string.Equals(t.Artist, artist.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.AlbumArtist, artist.Name, StringComparison.OrdinalIgnoreCase)).ToList();

        if (TrackList.SelectedItem is LibraryGenre genre)
            return Repository.GetTracksForGenre(genre.Name).ToList();

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
                return Repository.GetTracksForAlbum(album.Album).ToList();
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
            return Repository.GetTracksForAlbum(selectedAlbum.Album).ToList();

        if (TrackList.SelectedItem is LibraryArtist artist)
            return Repository.GetAllTracks().Where(t =>
                string.Equals(t.Artist, artist.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.AlbumArtist, artist.Name, StringComparison.OrdinalIgnoreCase)).ToList();

        if (TrackList.SelectedItem is LibraryGenre genre)
            return Repository.GetTracksForGenre(genre.Name).ToList();

        if (_browseMode == "playlist" && _selectedPlaylistId is long playlistId &&
            TrackList.SelectedItem is null)
            return Repository.GetPlaylistTracks(playlistId).ToList();

        if (_browseMode == "album-tracks" && !string.IsNullOrWhiteSpace(_albumDrillDown) &&
            TrackList.SelectedItem is null)
            return Repository.GetTracksForAlbum(_albumDrillDown).ToList();

        if (_browseMode == "artist-tracks" && !string.IsNullOrWhiteSpace(_listDrillDown) &&
            TrackList.SelectedItem is null)
        {
            return Repository.GetAllTracks().Where(t =>
                string.Equals(t.Artist, _listDrillDown, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.AlbumArtist, _listDrillDown, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (_browseMode == "genre-tracks" && !string.IsNullOrWhiteSpace(_listDrillDown) &&
            TrackList.SelectedItem is null)
            return Repository.GetTracksForGenre(_listDrillDown).ToList();

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

        var playlist = _selectedPlaylistId is long pid ? Repository.GetPlaylist(pid) : null;
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
            Playback.AddToQueue(tracks);
    }

    private void ContextPlayNext_OnClick(object sender, RoutedEventArgs e)
    {
        var tracks = ResolveSelectedTracks();
        if (tracks.Count > 0)
            Playback.InsertAfterCurrent(tracks);
    }

    private void ContextShowInExplorer_OnClick(object sender, RoutedEventArgs e)
    {
        var track = ResolveSingleSelectedTrack() ?? Playback.CurrentTrack;
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
        if (Repository.GetPlaylist(playlistId) is { IsSmart: true })
            return;
        if (TrackList.SelectedItem is not LibraryTrack track)
            return;

        Repository.RemoveTrackFromPlaylist(playlistId, track.Id);
        RefreshPlaylistNav();
        RefreshLibraryViews();
        ViewTitleText.Text = $"Removed “{track.DisplayTitle}” from playlist";
    }

    private void ContextAddToPlaylist_OnClick(object sender, RoutedEventArgs e)
    {
        var tracks = ResolveSelectedTracks();
        if (tracks.Count == 0)
            return;

        var playlists = Repository.GetPlaylists();
        var manualPlaylists = playlists.Where(p => !p.IsSmart).ToList();
        if (manualPlaylists.Count == 0 && playlists.Count == 0)
        {
            NewPlaylist_OnClick(sender, e);
            playlists = Repository.GetPlaylists();
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
        var playlist = existing ?? Repository.CreatePlaylist(prompt.Result);
        Repository.AddTracksToPlaylist(playlist.Id, tracks.Select(t => t.Id));
        RefreshPlaylistNav();
        if (_browseMode == "playlist" && _selectedPlaylistId == playlist.Id)
            RefreshLibraryViews();
        ViewTitleText.Text = $"Added {tracks.Count} to “{playlist.Name}”";
    }

    private LyricsWindow? _lyricsWindow;

    private void Lyrics_OnClick(object sender, RoutedEventArgs e)
    {
        var track = Playback.CurrentTrack ?? ResolveSingleSelectedTrack();
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

        _lyricsWindow = new LyricsWindow(track, () => Playback.Position) { Owner = this };
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

        var tracks = Repository.GetPlaylistTracks(playlist.Id);
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
            var playlist = M3uPlaylistService.Import(Repository, dlg.FileName);
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
        Playback.SetVolume(VolumeSlider.Value);
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
        App.SaveSettings();
        Playback.ReloadOutputSettings();
    }

    private void Mute_OnClick(object sender, RoutedEventArgs e)
    {
        if (_muted)
        {
            _muted = false;
            _suppressVolumeSlider = true;
            Playback.SetVolume(_volumeBeforeMute);
            if (VolumeSlider is not null)
                VolumeSlider.Value = _volumeBeforeMute;
            _suppressVolumeSlider = false;
        }
        else
        {
            _volumeBeforeMute = Math.Max(0.05, Playback.Volume);
            _muted = true;
            _suppressVolumeSlider = true;
            Playback.SetVolume(0);
            if (VolumeSlider is not null)
                VolumeSlider.Value = 0;
            _suppressVolumeSlider = false;
        }

        UpdateMuteChrome();
    }

    private void AdjustVolume(double delta)
    {
        _muted = false;
        var next = Math.Clamp(Playback.Volume + delta, 0, 1);
        _suppressVolumeSlider = true;
        Playback.SetVolume(next);
        if (VolumeSlider is not null)
            VolumeSlider.Value = next;
        _suppressVolumeSlider = false;
        UpdateMuteChrome();
    }

    private void UpdateMuteChrome()
    {
        if (MuteButton is null)
            return;
        MuteButton.Content = Playback.Volume <= 0.001 || _muted ? "🔇" : "🔊";
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
                _sleepTimer.Start(TimeSpan.FromMinutes(minutes), Playback.Volume);
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
            _sleepTimer.StartEndOfTrack(Playback.Volume);
            UpdateSleepTimerChrome();
        };
        menu.Items.Add(endTrack);
        var endQueue = new MenuItem { Header = "End of queue" };
        endQueue.Click += (_, _) =>
        {
            _sleepTimer.StartEndOfQueue(Playback.Volume);
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

        _miniPlayer = new MiniPlayerWindow(Playback, () => NowPlayingCover.Source)
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
        var track = Playback.CurrentTrack;
        if (track is null)
        {
            Tray.UpdateTooltip(null, null);
            return;
        }

        Tray.UpdateTooltip(track.DisplayTitle, track.DisplayArtist);
        Tray.NotifyTrackChanged(track.DisplayTitle, track.DisplayArtist);
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
            var released = Playback.ReleaseCurrentFileIfMatches(track.FilePath);
            AudioTagWriter.Write(dlg.Result);
            Repository.UpdateTrackMetadata(dlg.Result);
            if (released is { } state)
                Playback.ResumeAfterFileRelease(state.WasPlaying, state.Position);

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
            return Repository.GetTracksForAlbum(_albumDrillDown).ToList();
        if (_browseMode == "artist-tracks" && !string.IsNullOrWhiteSpace(_listDrillDown))
        {
            return Repository.GetAllTracks().Where(t =>
                string.Equals(t.Artist, _listDrillDown, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.AlbumArtist, _listDrillDown, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (_browseMode == "genre-tracks" && !string.IsNullOrWhiteSpace(_listDrillDown))
            return Repository.GetTracksForGenre(_listDrillDown).ToList();
        if (_selectedAlbum is not null)
            return Repository.GetTracksForAlbum(_selectedAlbum.Album).ToList();
        if (TrackList.SelectedItem is LibraryAlbum album)
            return Repository.GetTracksForAlbum(album.Album).ToList();
        if (TrackList.SelectedItem is LibraryTrack track && !string.IsNullOrWhiteSpace(track.Album))
            return Repository.GetTracksForAlbum(track.Album).ToList();
        return [];
    }

    private void StartLyricsDownload(IReadOnlyList<LibraryTrack> tracks, string label)
    {
        _ = Task.Run(() =>
        {
            var queued = EnsureLyricsPrefetch(true).QueueDownload(tracks, LyricsQueueMode.Manual);
            Dispatcher.BeginInvoke(() =>
            {
                if (queued == 0)
                {
                    ViewTitleText.Text = $"Lyrics already cached for this {label}";
                    return;
                }

                ViewTitleText.Text = $"Downloading lyrics for {label}: 0/{queued}…";
            });
        });
    }

    private async void EditAlbum_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var seed = ResolveCoverSeedTrack();
            if (seed is null && _browseMode == "album-tracks" && !string.IsNullOrWhiteSpace(_albumDrillDown))
                seed = Repository.GetTracksForAlbum(_albumDrillDown).FirstOrDefault();
            if (seed is null && _selectedAlbum is not null)
                seed = Repository.GetTracksForAlbum(_selectedAlbum.Album).FirstOrDefault();
            if (seed is null)
            {
                ViewTitleText.Text = "Select or open an album to edit";
                return;
            }

            var albumTracks = string.IsNullOrWhiteSpace(seed.Album)
                ? new List<LibraryTrack> { seed }
                : Repository.GetTracksForAlbum(seed.Album).ToList();
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
            var newYear = dlg.ResultYear;
            var newGenre = dlg.ResultGenre ?? "";
            var newDateReleased = dlg.ResultDateReleased ?? "";
            var newComment = dlg.ResultComment ?? "";
            var trackEdits = dlg.ResultTracks.ToDictionary(r => r.Id);
            var updateCover = dlg.UpdateCover;
            var clearCover = dlg.ClearCover;
            var cover = updateCover && dlg.ResultCover is { Length: > 0 }
                ? CoverArtHelper.NormalizeDownloadedCover(dlg.ResultCover, outputSize: 1200, quality: 90) ?? dlg.ResultCover
                : dlg.ResultCover;

            EditAlbumButton.IsEnabled = false;
            ViewTitleText.Text = $"Updating album 0/{albumTracks.Count}…";

            (bool WasPlaying, TimeSpan Position)? resume = null;
            var current = Playback.CurrentTrack;
            if (current is not null &&
                albumTracks.Any(t => string.Equals(t.FilePath, current.FilePath, StringComparison.OrdinalIgnoreCase)))
            {
                var released = Playback.ReleaseCurrentFileIfMatches(current.FilePath);
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

                        trackEdits.TryGetValue(track.Id, out var row);
                        var edited = new LibraryTrack
                        {
                            Id = track.Id,
                            FilePath = track.FilePath,
                            Title = row?.Title.Trim() ?? track.Title,
                            Artist = row?.Artist.Trim() ?? track.Artist,
                            AlbumArtist = newArtist,
                            Album = newAlbum,
                            TrackNumber = row?.TrackNumber ?? track.TrackNumber,
                            Year = newYear,
                            Genre = newGenre,
                            DateReleased = newDateReleased,
                            Comment = newComment,
                            Duration = track.Duration,
                            Bitrate = track.Bitrate,
                            Format = track.Format,
                            DateAddedUtc = track.DateAddedUtc,
                            FileModifiedUtc = DateTime.UtcNow,
                            CoverArt = coverArt,
                            PlayCount = track.PlayCount,
                            LastPlayedUtc = track.LastPlayedUtc,
                            Rating = Math.Clamp(row?.Rating ?? track.Rating, 0, 5),
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
                            Thread.Sleep(300);
                            AudioTagWriter.Write(edited);
                            if (clearCover)
                                CoverArtHelper.ClearCoverFromFile(track.AudioFilePath);
                        }

                        if (edited.Id > 0)
                        {
                            Repository.UpdateTrackMetadata(edited);
                            if (edited.Rating != track.Rating)
                                Repository.SetRating(edited.Id, edited.Rating);
                            if (clearCover)
                                Repository.UpdateCoverArt(edited.Id, null);
                            else if (updateCover && cover is { Length: > 0 })
                                Repository.UpdateCoverArt(edited.Id, cover);
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
                Playback.ResumeAfterFileRelease(playState.WasPlaying, playState.Position);

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
                    Year = newYear ?? _selectedAlbum.Year,
                    CoverArt = clearCover ? null : cover ?? _selectedAlbum.CoverArt,
                    IsSelected = true,
                };
            }

            if (_browseMode is "albums" or "album-tracks")
                _browseMode = "album-tracks";
            RefreshLibraryViews();
            RefreshQueueList();
            RefreshNowPlaying();
            ViewTitleText.Text = errors == 0
                ? $"Updated album on {updated} track(s)"
                : $"Updated {updated} track(s), {errors} failed";
        }
        catch (Exception ex)
        {
            ViewTitleText.Text = "Album update failed";
            MessageBox.Show(this,
                $"Could not update the album:\n\n{ex.Message}",
                "Local Music Hub",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            EditAlbumButton.IsEnabled = true;
        }
    }

    private LibraryTrack? ResolveCoverSeedTrack()
    {
        if (TrackList.SelectedItem is LibraryTrack selected)
            return selected;

        if (_browseMode is "albums" or "home")
        {
            var album = _selectedAlbum;
            if (album is not null)
                return Repository.GetTracksForAlbum(album.Album).FirstOrDefault(t => t.CoverArt is { Length: > 0 })
                       ?? Repository.GetTracksForAlbum(album.Album).FirstOrDefault();
        }

        if (_browseMode == "album-tracks" && !string.IsNullOrWhiteSpace(_albumDrillDown))
            return Repository.GetTracksForAlbum(_albumDrillDown).FirstOrDefault(t => t.CoverArt is { Length: > 0 })
                   ?? Repository.GetTracksForAlbum(_albumDrillDown).FirstOrDefault();

        if (QueueList.SelectedItem is LibraryTrack queued)
            return queued;

        return Playback.CurrentTrack is { Id: > 0 } current
            ? Repository.GetTrackById(current.Id) ?? current
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
            Repository.UpdateTrackMetadata(dlg.Result);
            Repository.SetRating(dlg.Result.Id, dlg.Result.Rating);
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
        var current = Playback.CurrentTrack;

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
                        var released = Dispatcher.Invoke(() => Playback.ReleaseCurrentFileIfMatches(track.FilePath));
                        if (released is { } state)
                            resume = (state.WasPlaying, state.Position);
                    }

                    if (patch.HasFileTagChanges)
                        AudioTagWriter.Write(updated);

                    Dispatcher.Invoke(() =>
                    {
                        Repository.UpdateTrackMetadata(updated);
                        if (patch.ApplyRating)
                            Repository.SetRating(updated.Id, updated.Rating);
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
            Playback.ResumeAfterFileRelease(playState.WasPlaying, playState.Position);

        RefreshLibraryViews();
        RefreshQueueList();
        RefreshNowPlaying();
        ViewTitleText.Text = failed == 0
            ? $"Updated tags on {done} track{(done == 1 ? "" : "s")}"
            : $"Updated {done} track{(done == 1 ? "" : "s")}; {failed} failed";
    }

    private void SetTrackRating(long trackId, int rating)
    {
        Repository.SetRating(trackId, rating);
        RefreshLibraryViews();
        RefreshQueueList();
        RefreshNowPlaying();
    }

    private void NewSmartPlaylist_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new SmartPlaylistEditorWindow(repository: Repository) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultRules is null || string.IsNullOrWhiteSpace(dlg.ResultName))
            return;

        var playlist = Repository.CreateSmartPlaylist(dlg.ResultName, dlg.ResultRules);
        SelectPlaylist(playlist);
    }

    private void EditSmartPlaylist_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetPlaylistFromNode(PlaylistNavTree.SelectedItem) is not LibraryPlaylist playlist || !playlist.IsSmart)
            return;

        var dlg = new SmartPlaylistEditorWindow(playlist.Name, playlist.Rules, isEdit: true, repository: Repository) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultRules is null || string.IsNullOrWhiteSpace(dlg.ResultName))
            return;

        Repository.UpdateSmartPlaylist(playlist.Id, dlg.ResultName, dlg.ResultRules);
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
        TrySelectTreeNode(FindPlaylistNode(Repository.GetPlaylistTree(), playlist.Id));
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

        Repository.CreatePlaylistFolder(dlg.Result, parentId);
        RefreshPlaylistNav();
    }

    private void RenamePlaylistFolder_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetFolderFromNode(PlaylistNavTree.SelectedItem)?.FolderId is not long folderId)
            return;

        var folder = Repository.GetPlaylistFolders().FirstOrDefault(f => f.Id == folderId);
        if (folder is null)
            return;

        var dlg = new TextPromptWindow("Rename folder", "New name:", folder.Name) { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result))
            return;

        Repository.RenamePlaylistFolder(folderId, dlg.Result);
        RefreshPlaylistNav();
    }

    private void DeletePlaylistFolder_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetFolderFromNode(PlaylistNavTree.SelectedItem)?.FolderId is not long folderId)
            return;

        var folder = Repository.GetPlaylistFolders().FirstOrDefault(f => f.Id == folderId);
        if (folder is null)
            return;

        var confirm = MessageBox.Show(this,
            $"Delete folder “{folder.Name}”? Playlists inside will move to the top level.",
            "Delete folder",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        Repository.DeletePlaylistFolder(folderId);
        RefreshPlaylistNav();
    }

    private void MovePlaylistToFolder_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetPlaylistFromNode(PlaylistNavTree.SelectedItem)?.Id is not long playlistId)
            return;

        var folders = Repository.GetPlaylistFolders();
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
                folder = Repository.CreatePlaylistFolder(dlg.Result.Trim());
            }

            folderId = folder.Id;
        }

        Repository.MovePlaylistToFolder(playlistId, folderId);
        RefreshPlaylistNav();
    }

    private void NewPlaylist_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new TextPromptWindow("New playlist", "Playlist name:") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result))
            return;

        var playlist = Repository.CreatePlaylist(dlg.Result);
        SelectPlaylist(playlist);
    }

    private void RenamePlaylist_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetPlaylistFromNode(PlaylistNavTree.SelectedItem) is not LibraryPlaylist playlist)
            return;

        var dlg = new TextPromptWindow("Rename playlist", "New name:", playlist.Name) { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result))
            return;

        Repository.RenamePlaylist(playlist.Id, dlg.Result);
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

        Repository.DeletePlaylist(playlist.Id);
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
        if (index < 0 || index >= Playback.Queue.Count)
            return;

        // Play from here: drop tracks before the clicked item so the queue matches.
        var fromHere = Playback.Queue.Skip(index).ToList();
        Playback.SetQueue(fromHere, 0);
        Playback.Play();
    }

    private void QueueMoveUp_OnClick(object sender, RoutedEventArgs e)
    {
        var index = QueueList.SelectedIndex;
        if (index <= 0)
            return;
        Playback.MoveInQueue(index, index - 1);
        QueueList.SelectedIndex = index - 1;
    }

    private void QueueMoveDown_OnClick(object sender, RoutedEventArgs e)
    {
        var index = QueueList.SelectedIndex;
        if (index < 0 || index >= Playback.Queue.Count - 1)
            return;
        Playback.MoveInQueue(index, index + 1);
        QueueList.SelectedIndex = index + 1;
    }

    private void QueueRemove_OnClick(object sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedIndex < 0)
            return;
        Playback.RemoveFromQueue(QueueList.SelectedIndex);
    }

    private void QueueClear_OnClick(object sender, RoutedEventArgs e) =>
        Playback.ClearQueue(stopPlayback: false);

    private void PlayPause_OnClick(object sender, RoutedEventArgs e)
    {
        if (Playback.CurrentTrack is null)
        {
            PlayCurrentSelection(replaceQueue: true);
            return;
        }

        Playback.TogglePlayPause();
    }

    private void Previous_OnClick(object sender, RoutedEventArgs e) => Playback.Previous();
    private void Next_OnClick(object sender, RoutedEventArgs e) => Playback.Next();

    private void Shuffle_OnClick(object sender, RoutedEventArgs e)
    {
        var enabled = !Playback.ShuffleEnabled;
        Playback.SetShuffle(enabled);
        App.Settings.Shuffle = enabled;
        App.SaveSettings();
        UpdateShuffleRepeatChrome();
    }

    private void Repeat_OnClick(object sender, RoutedEventArgs e)
    {
        var mode = Playback.CycleRepeatMode();
        App.Settings.RepeatMode = mode.ToStorageValue();
        App.SaveSettings();
        UpdateShuffleRepeatChrome();
    }

    private void UpdateShuffleRepeatChrome()
    {
        if (ShuffleButton is not null)
            ShuffleButton.Opacity = Playback.ShuffleEnabled ? 1.0 : 0.45;

        if (ShuffleIcon is not null)
        {
            ShuffleIcon.Fill = Playback.ShuffleEnabled
                ? (FindResource("HubAccentBrush") as System.Windows.Media.Brush)
                : (FindResource("HubTextPrimaryBrush") as System.Windows.Media.Brush);
        }

        if (RepeatButton is null)
            return;

        var repeatOn = Playback.RepeatMode != PlaybackRepeatMode.Off;
        RepeatButton.Opacity = repeatOn ? 1.0 : 0.45;
        if (RepeatIcon is not null)
        {
            RepeatIcon.Fill = repeatOn
                ? (FindResource("HubAccentBrush") as System.Windows.Media.Brush)
                : (FindResource("HubTextPrimaryBrush") as System.Windows.Media.Brush);
        }

        if (RepeatOneBadge is not null)
            RepeatOneBadge.Visibility = Playback.RepeatMode == PlaybackRepeatMode.One
                ? Visibility.Visible
                : Visibility.Collapsed;

        RepeatButton.ToolTip = Playback.RepeatMode switch
        {
            PlaybackRepeatMode.One => "Repeat one",
            PlaybackRepeatMode.All => "Repeat all",
            _ => "Repeat off",
        };
    }

    private void NowPlayingRating_OnRatingChanged(object? sender, int rating)
    {
        if (Playback.CurrentTrack is null)
            return;
        SetTrackRating(Playback.CurrentTrack.Id, rating);
    }

    private void NowPlayingRate_OnClick(object sender, RoutedEventArgs e)
    {
        if (Playback.CurrentTrack is null)
            return;
        if (sender is not FrameworkElement { Tag: string tag } || !int.TryParse(tag, out var rating))
            return;
        SetTrackRating(Playback.CurrentTrack.Id, rating);
    }

    private void HookPositionSliderThumb()
    {
        if (PositionSlider is null)
            return;

        PositionSlider.ApplyTemplate();

        // IsMoveToPointEnabled marks mouse events handled; still need release to commit the seek.
        PositionSlider.AddHandler(
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(PositionSlider_OnSeekMouseUp),
            handledEventsToo: true);

        if (FindVisualChild<Thumb>(PositionSlider) is not Thumb thumb)
            return;

        thumb.DragStarted += (_, _) => BeginPositionScrub();
        thumb.DragCompleted += (_, _) => EndPositionScrub();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private void PositionSlider_OnPreviewMouseDown(object sender, MouseButtonEventArgs e) => BeginPositionScrub();

    private void PositionSlider_OnSeekMouseUp(object sender, MouseButtonEventArgs e) => EndPositionScrub();

    private void PositionSlider_OnLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e) => EndPositionScrub();

    private void PositionSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressPositionSlider)
            return;

        // Click-to-point can update Value before PreviewMouseDown runs.
        if (!_positionScrubbing && Mouse.LeftButton == MouseButtonState.Pressed)
            BeginPositionScrub();

        if (!_positionScrubbing)
            return;

        UpdatePositionLabelsFromSlider();
    }

    private void BeginPositionScrub()
    {
        if (_positionScrubbing)
            return;

        _positionScrubbing = true;
        _positionSeekCommitted = false;
        SetPositionRenderActive(false);
    }

    private void EndPositionScrub()
    {
        if (!_positionScrubbing || _positionSeekPending)
            return;

        _positionSeekPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _positionSeekPending = false;
            if (_positionScrubbing)
                CommitPositionSeek();
            _positionScrubbing = false;
        }, DispatcherPriority.Input);
    }

    private void CommitPositionSeek()
    {
        if (_positionSeekCommitted || Playback.Duration <= TimeSpan.Zero)
            return;

        _positionSeekCommitted = true;

        var duration = Playback.Duration;
        var fraction = PositionSlider.Value;
        var target = TimeSpan.FromSeconds(fraction * duration.TotalSeconds);
        Playback.Seek(target);
        SyncPositionAnchor();
        UpdatePositionRenderState();

        _suppressPositionSlider = true;
        PositionSlider.Value = fraction;
        _suppressPositionSlider = false;
        SetPositionLabels(target, duration);
    }

    private void UpdatePositionLabelsFromSlider()
    {
        if (Playback.CurrentTrack is null)
            return;

        var duration = Playback.Duration;
        if (duration.TotalSeconds <= 0)
            return;

        var preview = TimeSpan.FromSeconds(PositionSlider.Value * duration.TotalSeconds);
        SetPositionLabels(preview, duration);
    }

    private void SetPositionLabels(TimeSpan position, TimeSpan duration)
    {
        ElapsedText.Text = FormatTime(position);
        RemainingText.Text = duration > position
            ? $"-{FormatTime(duration - position)}"
            : "0:00";
    }

    private void SyncPositionAnchor()
    {
        if (_playbackService?.CurrentTrack is null)
            return;

        _positionAnchor = Playback.Position;
        _positionAnchorDuration = Playback.Duration;
        _positionAnchorUtc = DateTime.UtcNow;
        if (_positionAnchorDuration.TotalSeconds > 0)
            _smoothedSliderFraction = _positionAnchor.TotalSeconds / _positionAnchorDuration.TotalSeconds;
    }

    private static double PlaybackSpeedFactor() =>
        Math.Clamp(App.Settings.PlaybackSpeed, 0.5, 2.0);

    private void ReconcilePositionDrift()
    {
        if (_playbackService?.CurrentTrack is null)
            return;

        if (Playback.IsInCrossfadeTransition)
            return;

        _positionAnchorDuration = Playback.Duration;
        var actual = Playback.Position;
        var predicted = _positionAnchor + TimeSpan.FromSeconds(
            (DateTime.UtcNow - _positionAnchorUtc).TotalSeconds * PlaybackSpeedFactor());
        var errorMs = (actual - predicted).TotalMilliseconds;

        if (Math.Abs(errorMs) > 2000)
        {
            SyncPositionAnchor();
            return;
        }

        // Short clips: reader position updates in coarse steps — skip drift steering.
        if (_positionAnchorDuration.TotalSeconds is > 0 and < 20)
            return;

        // Only catch up when behind; never steer backward (that caused visible bounce).
        if (errorMs > 20)
            _positionAnchorUtc -= TimeSpan.FromMilliseconds(Math.Min(60, errorMs * 0.4));
    }

    private TimeSpan GetInterpolatedPosition()
    {
        if (_playbackService?.CurrentTrack is null)
            return TimeSpan.Zero;

        if (!_playbackService.IsPlaying || _playbackService.IsPaused)
            return Playback.Position;

        var elapsedSeconds = (DateTime.UtcNow - _positionAnchorUtc).TotalSeconds * PlaybackSpeedFactor();
        var position = _positionAnchor + TimeSpan.FromSeconds(elapsedSeconds);
        if (position < TimeSpan.Zero)
            position = TimeSpan.Zero;

        if (_positionAnchorDuration > TimeSpan.Zero && position > _positionAnchorDuration)
            return _positionAnchorDuration;

        return position;
    }

    private void SetPositionRenderActive(bool active)
    {
        if (active == _positionRenderActive)
            return;

        _positionRenderActive = active;
        if (active)
        {
            _positionRenderingHandler ??= OnPositionRendering;
            CompositionTarget.Rendering += _positionRenderingHandler;
        }
        else if (_positionRenderingHandler is not null)
        {
            CompositionTarget.Rendering -= _positionRenderingHandler;
        }
    }

    private void UpdatePositionRenderState()
    {
        var inCrossfade = Playback.IsInCrossfadeTransition;
        var shouldRender = _playbackService?.CurrentTrack is not null
            && (Playback.IsPlaying || inCrossfade)
            && !Playback.IsPaused
            && !_positionScrubbing;
        if (shouldRender && !_positionRenderActive)
            SyncPositionAnchor();
        SetPositionRenderActive(shouldRender);
    }

    private void OnPositionRendering(object? sender, EventArgs e)
    {
        if (_positionScrubbing || _playbackService?.CurrentTrack is null)
            return;

        if ((!_playbackService.IsPlaying || _playbackService.IsPaused) && !Playback.IsInCrossfadeTransition)
        {
            SetPositionRenderActive(false);
            return;
        }

        UpdatePositionUi(GetInterpolatedPosition(), _positionAnchorDuration);
    }

    private void UpdatePositionUi(TimeSpan position, TimeSpan duration, bool allowBackward = false)
    {
        if (_positionScrubbing)
            return;

        if (duration.TotalSeconds > 0)
        {
            var target = Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1);
            if (allowBackward)
                _smoothedSliderFraction += (target - _smoothedSliderFraction) * 0.55;
            else
                _smoothedSliderFraction = Math.Max(_smoothedSliderFraction, target);

            SetPositionSliderFraction(_smoothedSliderFraction);
        }

        // Keep labels stable on very short tracks; slider still moves every frame.
        var labelPosition = duration.TotalSeconds < 30
            ? TimeSpan.FromMilliseconds(Math.Round(position.TotalMilliseconds / 100.0) * 100.0)
            : position;
        SetPositionLabels(labelPosition, duration);
    }

    private void SetPositionSliderFraction(double fraction)
    {
        _suppressPositionSlider = true;
        PositionSlider.Value = fraction;
        _suppressPositionSlider = false;
    }

    private void PositionTimer_OnTick(object? sender, EventArgs e)
    {
        if (ShutdownRequestService.TryConsumeShutdownRequest())
        {
            _forceClose = true;
            App.SaveSettings();
            System.Windows.Application.Current.Shutdown();
            return;
        }

        if (_positionScrubbing || _playbackService?.CurrentTrack is null)
        {
            SetPositionRenderActive(false);
            return;
        }

        if (_positionRenderActive)
            ReconcilePositionDrift();
        else
            UpdatePositionUi(Playback.Position, Playback.Duration, allowBackward: false);

        UpdatePositionRenderState();

        Playback.UpdateTransitionState();
        if (Playback.TryAdvanceAtCueEnd())
            return;

        LastFm.OnPositionTick(
            Playback.CurrentTrack,
            Playback.Position,
            Playback.Duration,
            App.Settings);
    }

    private void UpdateDiscordPresence()
    {
        if (_playbackService is null || _discordService is null)
            return;

        Discord.Update(Playback.CurrentTrack, Playback.IsPlaying);
    }

    private void RefreshNowPlaying()
    {
        var track = _playbackService?.CurrentTrack;
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
            _smoothedSliderFraction = 0;
            SetPositionSliderFraction(0);
            SetPositionLabels(TimeSpan.Zero, TimeSpan.Zero);
            NowPlayingCover.Source = null;
            Tray.UpdateTooltip(null, null);
            UpdatePlayPauseChrome();
            _miniPlayer?.Refresh();
            return;
        }

        // Refresh stats from DB when possible.
        var live = track.Id > 0 ? Repository.GetTrackById(track.Id) ?? track : track;

        NowPlayingTitle.Text = live.DisplayTitle;
        NowPlayingArtist.Text = $"{live.DisplayArtist} — {live.DisplayAlbum}";
        NowPlayingStats.Text = live.PlayStatsLabel;
        if (NowPlayingRating is not null)
        {
            NowPlayingRating.IsEnabled = true;
            NowPlayingRating.SyncRating(Math.Clamp(live.Rating, 0, 5));
        }
        SetPlayPauseIcons(Playback.IsPlaying);
        UpdatePlayPauseChrome();
        UpdateShuffleRepeatChrome();
        Tray.UpdateTooltip(live.DisplayTitle, live.DisplayArtist);

        var duration = Playback.Duration;
        var position = Playback.Position;
        if (!_positionScrubbing)
        {
            SyncPositionAnchor();
            UpdatePositionRenderState();
            if (!_positionRenderActive)
                UpdatePositionUi(position, duration, allowBackward: false);
        }

        NowPlayingCover.Source = CoverArtHelper.ToBitmap(live.CoverArt, 288, centerCropSquare: true);
        _miniPlayer?.Refresh();
        if (ArtistPanel?.Visibility == Visibility.Visible)
            _ = RefreshArtistPanelAsync(live.DisplayArtist);
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private void LibraryStats_OnClick(object sender, RoutedEventArgs e)
    {
        var stats = Repository.GetStatistics();
        var dlg = new StatisticsWindow(stats) { Owner = this };
        dlg.ShowDialog();
    }

    private void FindDuplicates_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new DuplicatesWindow(Repository) { Owner = this };
        dlg.LibraryChanged += (_, _) =>
        {
            PurgeMissingFromQueue();
            RefreshLibraryViews();
        };
        dlg.ShowDialog();
    }

    private void PurgeMissingFromQueue()
    {
        for (var i = Playback.Queue.Count - 1; i >= 0; i--)
        {
            try
            {
                if (!File.Exists(Playback.Queue[i].AudioFilePath))
                    Playback.RemoveFromQueue(i);
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
            tracks = Repository.GetAllTracks().ToList();

        if (tracks.Count == 0)
        {
            ViewTitleText.Text = "No tracks to organize";
            return;
        }

        var dlg = new OrganizePreviewWindow(Repository, tracks, FolderWatcher, Playback) { Owner = this };
        dlg.LibraryChanged += (_, _) => RefreshLibraryViews();
        dlg.ShowDialog();
    }

    private async void ScanReplayGain_OnClick(object sender, RoutedEventArgs e)
    {
        var tracks = Repository.GetAllTracks();
        if (tracks.Count == 0)
        {
            ViewTitleText.Text = "No tracks to scan";
            return;
        }

        ViewTitleText.Text = $"Scanning ReplayGain 0/{tracks.Count}…";
        try
        {
            var scanner = new ReplayGainScanner(Repository);
            var progress = new Progress<(int Done, int Total, string Path)>(p =>
                ViewTitleText.Text = $"Scanning ReplayGain {p.Done}/{p.Total}…");
            await scanner.ScanAsync(tracks, progress).ConfigureAwait(true);
            RefreshLibraryViews();
            ViewTitleText.Text = $"ReplayGain scan complete ({tracks.Count} tracks)";
        }
        catch (Exception ex)
        {
            ViewTitleText.Text = "ReplayGain scan failed";
            MessageBox.Show(this, ex.Message, "ReplayGain scan failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
        var artist = Playback.CurrentTrack?.DisplayArtist ?? "";
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

        var stats = Repository.GetArtistStats(artist);
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
        Repository.SetReviewStatus(tracks.Select(t => t.Id), "done");
        RefreshLibraryViews();
        ViewTitleText.Text = $"Marked {tracks.Count} track(s) done";
    }

    private async void ContextAcoustId_OnClick(object sender, RoutedEventArgs e)
    {
        var track = ResolveSelectedTracks().FirstOrDefault();
        if (track is null)
            return;
        if (!AcoustId.IsConfigured)
        {
            MessageBox.Show(this, "Set AcoustID API key and fpcalc path in Settings first.", "AcoustID",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ViewTitleText.Text = $"Identifying “{track.DisplayTitle}” with AcoustID…";
        var result = await AcoustId.IdentifyTrackAsync(Repository, track).ConfigureAwait(true);
        ViewTitleText.Text = result.Message;
        if (result.Success)
            RefreshLibraryViews();
    }

    public void ShowUpdateAvailable(UpdateCheckResult result)
    {
        _pendingUpdate = result;
        UpdateAvailabilityCache.Set(result.LatestVersion, result.InstallerDownloadUrl);
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
        var dlg = new SettingsWindow(
            () => _lyricsPrefetchService,
            () => Repository.GetAllTracks(),
            () => EnsureLyricsPrefetch(true)) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            HubTheme.ApplyFromSettings();
            Tray.ApplyTheme();
            UpdateShuffleRepeatChrome();
            Playback.SetVolume(App.Settings.DefaultVolume);
            Playback.ReloadOutputSettings();
            SyncSpeedComboFromSettings();
            DownloaderBridge.Apply(App.Settings);
            ApplyLibraryIngestHost();
            Discord.ApplySettings(App.Settings);
            ScriptHooks.Enabled = App.Settings.ScriptHooksEnabled;
            if (ScriptHooks.Enabled)
                ScriptHooks.EnsureScriptsFolder();
            if (App.Settings.AutoDownloadLyrics)
                EnsureLyricsPrefetchOnBackground(true);
            else if (_lyricsPrefetchService is not null)
                _lyricsPrefetchService.Enabled = false;
            UpdateDiscordPresence();
            ApplyFolderWatcher();
            ApplyDownloaderSidebarLayout();
            RefreshDownloaderStatus();
            OnTrayPreferenceChanged();
            if (dlg.RescanRequested)
                _ = ScanLibraryAsync();
        }
        else
        {
            UpdateShuffleRepeatChrome();
        }
    }
}
