using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LocalMusicHub.Models;
using LocalMusicHub.Services;
using MessageBox = System.Windows.MessageBox;

namespace LocalMusicHub;

public partial class SettingsWindow
{
    public bool RescanRequested { get; private set; }

    private readonly LyricsPrefetchService? _lyricsPrefetch;
    private readonly Func<IReadOnlyList<LibraryTrack>>? _getAllTracks;
    private List<float> _draftCustomEqBands = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    private UpdateCheckResult? _lastCheck;

    public SettingsWindow(
        LyricsPrefetchService? lyricsPrefetch = null,
        Func<IReadOnlyList<LibraryTrack>>? getAllTracks = null)
    {
        _lyricsPrefetch = lyricsPrefetch;
        _getAllTracks = getAllTracks;
        HubTheme.Ensure(this);
        InitializeComponent();
        DefaultVolumeSlider.ValueChanged += (_, _) => UpdateVolumeLabel();
        PlaybackSpeedSlider.ValueChanged += (_, _) => UpdatePlaybackSpeedLabel();
        LoadFromSettings();
        if (_lyricsPrefetch is not null)
        {
            _lyricsPrefetch.ProgressChanged += LyricsPrefetch_OnProgressChanged;
            Closed += (_, _) => _lyricsPrefetch.ProgressChanged -= LyricsPrefetch_OnProgressChanged;
        }
    }

    private void DownloadAllLyrics_OnClick(object sender, RoutedEventArgs e)
    {
        if (_lyricsPrefetch is null || _getAllTracks is null)
        {
            LyricsDownloadStatusText.Text = "Lyrics download is unavailable in this window.";
            return;
        }

        var tracks = _getAllTracks();
        if (tracks.Count == 0)
        {
            LyricsDownloadStatusText.Text = "No tracks in the library.";
            return;
        }

        var alreadyHad = 0;
        var skippedNotFound = 0;
        foreach (var track in tracks)
        {
            if (LyricsService.HasLocalLyrics(track.FilePath))
                alreadyHad++;
            else if (LyricsNotFoundStore.IsMarked(track.FilePath))
                skippedNotFound++;
        }

        var queued = _lyricsPrefetch.QueueDownload(tracks, LyricsQueueMode.MissingOnly);
        LyricsDownloadStatusText.Text = queued == 0
            ? BuildLyricsIdleStatus(alreadyHad, skippedNotFound)
            : $"Queued {queued} track(s) — downloading in the background from LRCLIB…";
        if (DownloadAllLyricsButton is not null)
            DownloadAllLyricsButton.IsEnabled = queued > 0;
    }

    private void RetryFailedLyrics_OnClick(object sender, RoutedEventArgs e)
    {
        if (_lyricsPrefetch is null || _getAllTracks is null)
        {
            LyricsDownloadStatusText.Text = "Lyrics download is unavailable in this window.";
            return;
        }

        var notFoundPaths = new HashSet<string>(LyricsNotFoundStore.GetAllPaths(), StringComparer.OrdinalIgnoreCase);
        var tracks = _getAllTracks().Where(t => notFoundPaths.Contains(t.FilePath)).ToList();
        if (tracks.Count == 0)
        {
            LyricsDownloadStatusText.Text = "No failed lyrics lookups to retry.";
            return;
        }

        var queued = _lyricsPrefetch.QueueDownload(tracks, LyricsQueueMode.RetryFailed);
        LyricsDownloadStatusText.Text = queued == 0
            ? "No failed lyrics lookups to retry."
            : $"Retrying {queued} previously not-found track(s)…";
        if (RetryFailedLyricsButton is not null)
            RetryFailedLyricsButton.IsEnabled = queued > 0;
    }

    private static string BuildLyricsIdleStatus(int eAlreadyHad = 0, int eSkippedNotFound = 0)
    {
        if (eAlreadyHad > 0 && eSkippedNotFound > 0)
            return $"All set — {eAlreadyHad} already had lyrics, {eSkippedNotFound} skipped (not found).";
        if (eAlreadyHad > 0)
            return $"All set — {eAlreadyHad} track(s) already had lyrics.";
        if (eSkippedNotFound > 0)
            return $"All set — {eSkippedNotFound} skipped (previously not found).";
        return "All tracks already have lyrics cached.";
    }

    private void LyricsPrefetch_OnProgressChanged(object? sender, LyricsPrefetchProgress e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.Total <= 0 && e.AlreadyHad > 0 && e.IsIdle)
            {
                LyricsDownloadStatusText.Text = $"All set — {e.AlreadyHad} track(s) already had lyrics.";
                if (DownloadAllLyricsButton is not null)
                    DownloadAllLyricsButton.IsEnabled = true;
                return;
            }

            if (e.Total <= 0)
                return;

            if (e.IsIdle)
            {
                var skipped = e.SkippedNotFound > 0 ? $", skipped {e.SkippedNotFound}" : "";
                LyricsDownloadStatusText.Text =
                    $"Done — saved {e.Saved}, already had {e.AlreadyHad}, not found {e.NotFound}{skipped}.";
                if (DownloadAllLyricsButton is not null)
                    DownloadAllLyricsButton.IsEnabled = true;
                if (RetryFailedLyricsButton is not null)
                    RetryFailedLyricsButton.IsEnabled = true;
            }
            else
            {
                LyricsDownloadStatusText.Text =
                    $"Downloading lyrics {e.Done}/{e.Total}" +
                    (string.IsNullOrWhiteSpace(e.CurrentTitle) ? "…" : $" — {e.CurrentTitle}");
            }
        });
    }
    private void LoadFromSettings()
    {
        var s = App.Settings;
        var status = YouTubeDownloaderBridge.GetLinkStatus();

        DarkThemeBox.IsChecked = s.UseDarkTheme;
        SelectComboByTag(AccentThemeBox, HubTheme.NormalizeAccent(s.AccentTheme));
        WatchFoldersBox.IsChecked = s.WatchLibraryFolders;
        RescanOnSaveBox.IsChecked = s.RescanLibraryOnSave;
        IntegrateDownloaderBox.IsChecked = s.IntegrateYouTubeDownloader;
        ShowDownloaderSidebarBox.IsChecked = s.ShowYouTubeDownloaderSidebar;
        MinimizeToTrayBox.IsChecked = s.MinimizeToTray;
        StartWithWindowsBox.IsChecked = AutoStartService.IsEnabled() || s.StartWithWindows;
        NotifyOnTrackChangeBox.IsChecked = s.NotifyOnTrackChange;
        AutoCheckBox.IsChecked = s.AutoCheckUpdates;
        NotifyTrayOnUpdateBox.IsChecked = s.NotifyTrayOnUpdate;
        DefaultVolumeSlider.Value = Math.Clamp(s.DefaultVolume, 0, 1);
        PlaybackSpeedSlider.Value = Math.Clamp(s.PlaybackSpeed, 0.5, 2.0);
        UpdatePlaybackSpeedLabel();

        var primary = s.LibraryFolders.FirstOrDefault() ?? AppPaths.DefaultMusicFolder;
        OrganizeTemplateBox.Text = s.OrganizeTemplate;
        OrganizeRootBox.Text = s.OrganizeRoot ?? primary;
        CrossfadeSecondsBox.Text = s.CrossfadeSeconds.ToString();
        GaplessBox.IsChecked = s.GaplessEnabled;
        CrossfadeBox.IsChecked = s.CrossfadeEnabled;
        DiscordPresenceBox.IsChecked = s.DiscordRichPresenceEnabled;
        DiscordClientIdBox.Text = s.DiscordClientId ?? "";
        LastFmBox.IsChecked = s.LastFmScrobbleEnabled;
        LastFmApiKeyBox.Text = s.LastFmApiKey ?? "";
        LastFmApiSecretBox.Text = s.LastFmApiSecret ?? "";
        LastFmUsernameBox.Text = s.LastFmUsername ?? "";
        LastFmStatusText.Text = string.IsNullOrWhiteSpace(s.LastFmSessionKey)
            ? "Not connected"
            : "Connected";
        ScriptHooksBox.IsChecked = s.ScriptHooksEnabled;
        AutoDownloadLyricsBox.IsChecked = s.AutoDownloadLyrics;
        EmbedLyricsInTagsBox.IsChecked = s.EmbedLyricsInTags;
        MarkNewImportsAsInboxBox.IsChecked = s.MarkNewImportsAsInbox;
        AcoustIdApiKeyBox.Text = s.AcoustIdApiKey ?? "";
        FpcalcPathBox.Text = s.FpcalcPath ?? "";
        _draftCustomEqBands = s.CustomEqBands is { Count: > 0 }
            ? s.CustomEqBands.ToList()
            : [0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        PopulateOutputDevices();
        SelectComboByTag(OutputBackendBox, s.OutputBackend);
        SelectComboByTag(OutputDeviceBox, string.IsNullOrWhiteSpace(s.OutputDeviceId) ? "default" : s.OutputDeviceId);
        SelectComboByTag(ReplayGainBox, s.ReplayGainMode);
        SelectComboByTag(EqPresetBox, s.EqPreset);

        PrimaryFolderBox.Text = primary;
        var extras = s.LibraryFolders.Skip(1).ToList();
        ExtraFoldersBox.Text = string.Join(Environment.NewLine, extras);

        DownloaderMusicFolderBox.Text = status.MusicFolder ?? "(not detected)";
        DownloaderStatusText.Text = BuildDownloaderStatus(status);
        AboutText.Text =
            $"Version {App.VersionDisplay}\n" +
            $"Settings: {AppPaths.SettingsPath}\n" +
            $"Library database: {AppPaths.DatabasePath}\n\n" +
            "Pairs with YouTube Downloader for adding music to your local library.";

        UpdateVolumeLabel();
    }

    private static string BuildDownloaderStatus(DownloaderLinkStatus status)
    {
        if (!status.Installed)
            return "YouTube Downloader not detected. Install it and download music to a folder this app watches.";
        if (!status.SettingsFound)
            return "Downloader installed — run it once so settings.json exists.";
        if (!status.ExtensionEnabled)
            return "Linked — enable the browser extension API in YouTube Downloader to queue downloads from Music Hub.";
        return $"Linked — extension API on port {status.ExtensionPort}. Use the sidebar to paste a URL and download.";
    }

    private void PopulateOutputDevices()
    {
        OutputDeviceBox.Items.Clear();
        foreach (var device in AudioOutputFactory.ListOutputDevices())
            OutputDeviceBox.Items.Add(new ComboBoxItem { Content = device.Name, Tag = device.Id });
    }

    private static void SelectComboByTag(System.Windows.Controls.ComboBox box, string tag)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }

        if (box.Items.Count > 0)
            box.SelectedIndex = 0;
    }

    private void UpdateVolumeLabel() =>
        DefaultVolumeLabel.Text = $"{(int)Math.Round(DefaultVolumeSlider.Value * 100)}%";

    private void UpdatePlaybackSpeedLabel() =>
        PlaybackSpeedLabel.Text = $"{PlaybackSpeedSlider.Value:0.00}×";

    private static string? SelectedTag(System.Windows.Controls.ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string;

    private async void ConnectLastFm_OnClick(object sender, RoutedEventArgs e)
    {
        var apiKey = LastFmApiKeyBox.Text.Trim();
        var apiSecret = LastFmApiSecretBox.Text.Trim();
        var username = LastFmUsernameBox.Text.Trim();
        var password = LastFmPasswordBox.Password;
        if (apiKey.Length == 0 || apiSecret.Length == 0 || username.Length == 0 || password.Length == 0)
        {
            MessageBox.Show(this, "Enter API key, API secret, username, and password first.", "Last.fm",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LastFmStatusText.Text = "Connecting…";
        try
        {
            var session = await LastFmScrobbler.CreateSessionAsync(apiKey, apiSecret, username, password)
                .ConfigureAwait(true);
            App.Settings.LastFmApiKey = apiKey;
            App.Settings.LastFmApiSecret = apiSecret;
            App.Settings.LastFmUsername = username;
            App.Settings.LastFmSessionKey = session;
            App.Settings.LastFmScrobbleEnabled = true;
            LastFmBox.IsChecked = true;
            LastFmPasswordBox.Password = "";
            LastFmStatusText.Text = "Connected";
            MessageBox.Show(this, "Last.fm connected. Click Save to keep settings.", "Last.fm",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            LastFmStatusText.Text = "Not connected";
            MessageBox.Show(this, $"Could not connect:\n{ex.Message}", "Last.fm",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenScriptsFolder_OnClick(object sender, RoutedEventArgs e)
    {
        ScriptHookService.EnsureScriptsFolderStatic();
        Process.Start(new ProcessStartInfo
        {
            FileName = ScriptHookService.ScriptsDirectory,
            UseShellExecute = true,
        });
    }

    private void BrowsePrimaryFolder_OnClick(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose your primary music library folder",
            SelectedPath = Directory.Exists(PrimaryFolderBox.Text) ? PrimaryFolderBox.Text : AppPaths.DefaultMusicFolder,
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        PrimaryFolderBox.Text = dlg.SelectedPath;
    }

    private void OpenDownloaderMusicFolder_OnClick(object sender, RoutedEventArgs e) =>
        OpenPath(DownloaderMusicFolderBox.Text);

    private void OpenDownloaderData_OnClick(object sender, RoutedEventArgs e) =>
        OpenPath(AppPaths.YouTubeDownloaderDataDirectory);

    private static void OpenPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            MessageBox.Show("Folder not found.", "Local Music Hub", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Local Music Hub", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportSettings_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON settings (*.json)|*.json",
            FileName = "LocalMusicHub-settings.json",
        };
        if (dlg.ShowDialog() != true)
            return;

        if (SettingsTransferService.Export(ReadSettingsFromUi(), dlg.FileName))
            MessageBox.Show(this, "Settings exported.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(this, "Export failed.", "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ImportSettings_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON settings (*.json)|*.json" };
        if (dlg.ShowDialog() != true)
            return;

        var (settings, error) = SettingsTransferService.Import(dlg.FileName);
        if (settings is null)
        {
            MessageBox.Show(this, error ?? "Import failed.", "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        App.ReplaceSettings(settings);
        LoadFromSettings();
        MessageBox.Show(this, "Settings imported. Click Save to keep them.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private AppSettings ReadSettingsFromUi()
    {
        var folders = new List<string>();
        if (!string.IsNullOrWhiteSpace(PrimaryFolderBox.Text))
            folders.Add(PrimaryFolderBox.Text.Trim());

        folders.AddRange(ExtraFoldersBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0));

        return new AppSettings
        {
            UseDarkTheme = DarkThemeBox.IsChecked == true,
            AccentTheme = HubTheme.NormalizeAccent(SelectedTag(AccentThemeBox)),
            WatchLibraryFolders = WatchFoldersBox.IsChecked == true,
            RescanLibraryOnSave = RescanOnSaveBox.IsChecked == true,
            IntegrateYouTubeDownloader = IntegrateDownloaderBox.IsChecked == true,
            ShowYouTubeDownloaderSidebar = ShowDownloaderSidebarBox.IsChecked == true,
            YouTubeDownloaderSidebarCollapsed = App.Settings.YouTubeDownloaderSidebarCollapsed,
            MinimizeToTray = MinimizeToTrayBox.IsChecked == true || StartWithWindowsBox.IsChecked == true,
            StartWithWindows = StartWithWindowsBox.IsChecked == true,
            NotifyOnTrackChange = NotifyOnTrackChangeBox.IsChecked == true,
            DefaultVolume = DefaultVolumeSlider.Value,
            PlaybackSpeed = PlaybackSpeedSlider.Value,
            OutputBackend = SelectedTag(OutputBackendBox) ?? "waveout",
            OutputDeviceId = SelectedTag(OutputDeviceBox) is "default" ? null : SelectedTag(OutputDeviceBox),
            ReplayGainMode = SelectedTag(ReplayGainBox) ?? "off",
            EqPreset = SelectedTag(EqPresetBox) ?? "flat",
            GaplessEnabled = GaplessBox.IsChecked == true,
            CrossfadeEnabled = CrossfadeBox.IsChecked == true,
            CrossfadeSeconds = int.TryParse(CrossfadeSecondsBox.Text.Trim(), out var crossfade) ? Math.Clamp(crossfade, 1, 30) : 6,
            DiscordRichPresenceEnabled = DiscordPresenceBox.IsChecked == true,
            DiscordClientId = string.IsNullOrWhiteSpace(DiscordClientIdBox.Text) ? null : DiscordClientIdBox.Text.Trim(),
            LastFmScrobbleEnabled = LastFmBox.IsChecked == true,
            LastFmApiKey = string.IsNullOrWhiteSpace(LastFmApiKeyBox.Text) ? null : LastFmApiKeyBox.Text.Trim(),
            LastFmApiSecret = string.IsNullOrWhiteSpace(LastFmApiSecretBox.Text) ? null : LastFmApiSecretBox.Text.Trim(),
            LastFmUsername = string.IsNullOrWhiteSpace(LastFmUsernameBox.Text) ? null : LastFmUsernameBox.Text.Trim(),
            LastFmSessionKey = App.Settings.LastFmSessionKey,
            ScriptHooksEnabled = ScriptHooksBox.IsChecked == true,
            AutoDownloadLyrics = AutoDownloadLyricsBox.IsChecked == true,
            EmbedLyricsInTags = EmbedLyricsInTagsBox.IsChecked == true,
            MarkNewImportsAsInbox = MarkNewImportsAsInboxBox.IsChecked == true,
            LeftSidebarWidth = App.Settings.LeftSidebarWidth,
            RightSidebarWidth = App.Settings.RightSidebarWidth,
            LeftSidebarVisible = App.Settings.LeftSidebarVisible,
            RightSidebarVisible = App.Settings.RightSidebarVisible,
            AcoustIdApiKey = string.IsNullOrWhiteSpace(AcoustIdApiKeyBox.Text) ? null : AcoustIdApiKeyBox.Text.Trim(),
            FpcalcPath = string.IsNullOrWhiteSpace(FpcalcPathBox.Text) ? null : FpcalcPathBox.Text.Trim(),
            OrganizeTemplate = OrganizeTemplateBox.Text.Trim(),
            OrganizeRoot = string.IsNullOrWhiteSpace(OrganizeRootBox.Text) ? null : OrganizeRootBox.Text.Trim(),
            LibraryFolders = folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Shuffle = App.Settings.Shuffle,
            RepeatMode = App.Settings.RepeatMode,
            SleepTimerMinutes = App.Settings.SleepTimerMinutes,
            CustomEqBands = _draftCustomEqBands.ToList(),
            YouTubeDownloaderMusicFolder = App.Settings.YouTubeDownloaderMusicFolder,
            YouTubeDownloaderPort = App.Settings.YouTubeDownloaderPort,
            YouTubeDownloaderToken = App.Settings.YouTubeDownloaderToken,
            AutoCheckUpdates = AutoCheckBox.IsChecked == true,
            NotifyTrayOnUpdate = NotifyTrayOnUpdateBox.IsChecked == true,
            DismissedUpdateVersion = App.Settings.DismissedUpdateVersion,
            LastUpdateCheckUtc = App.Settings.LastUpdateCheckUtc,
        };
    }

    private async void CheckUpdates_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateStatusText.Text = "Checking GitHub…";
        DownloadInstallerButton.IsEnabled = false;
        _lastCheck = await UpdateCheckService.CheckLatestReleaseAsync();

        if (!_lastCheck.Success)
        {
            UpdateStatusText.Text = _lastCheck.ErrorMessage ?? "Check failed.";
            return;
        }

        if (_lastCheck.NoPublishedReleases)
        {
            UpdateStatusText.Text = "No published releases yet on GitHub.";
            return;
        }

        if (_lastCheck.IsNewerThanCurrent)
        {
            UpdateStatusText.Text = $"Newer version available: {_lastCheck.LatestVersion} (you have {App.VersionDisplay}).";
            DownloadInstallerButton.IsEnabled = !string.IsNullOrWhiteSpace(_lastCheck.InstallerDownloadUrl);
            UpdateAvailabilityCache.Set(_lastCheck.LatestVersion, _lastCheck.InstallerDownloadUrl);
        }
        else
        {
            UpdateStatusText.Text = $"You are up to date ({App.VersionDisplay}).";
            UpdateAvailabilityCache.Clear();
        }

        App.Settings.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        App.SaveSettings();
    }

    private async void DownloadInstaller_OnClick(object sender, RoutedEventArgs e)
    {
        if (_lastCheck?.InstallerDownloadUrl is not { } url)
        {
            UpdateCheckService.OpenUpdateDownload(null);
            return;
        }

        UpdateStatusText.Text = "Downloading installer…";
        DownloadInstallerButton.IsEnabled = false;
        var (path, error) = await UpdateCheckService.DownloadInstallerToTempAsync(url, _lastCheck.LatestVersion);
        if (path is null)
        {
            UpdateStatusText.Text = error ?? "Download failed.";
            DownloadInstallerButton.IsEnabled = true;
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            UpdateStatusText.Text = "Installer started.";
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = ex.Message;
            DownloadInstallerButton.IsEnabled = true;
        }
    }

    private void OpenReleases_OnClick(object sender, RoutedEventArgs e) =>
        UpdateCheckService.OpenUpdateDownload(null);

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        var previousFolders = App.Settings.LibraryFolders.ToList();
        var settings = ReadSettingsFromUi();
        YouTubeDownloaderBridge.SyncApiCredentials(settings);
        AutoStartService.SetEnabled(settings.StartWithWindows);
        if (settings.StartWithWindows)
            settings.MinimizeToTray = true;

        var foldersChanged = !FoldersEqual(previousFolders, settings.LibraryFolders);
        // Full reindex only when folders changed, or the user opted into "always rescan".
        RescanRequested = foldersChanged || settings.RescanLibraryOnSave;
        App.ReplaceSettings(settings);
        App.SaveSettings();
        DialogResult = true;
        Close();
    }

    private static bool FoldersEqual(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
            return false;
        var left = a.Select(f => f.Trim()).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        var right = b.Select(f => f.Trim()).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private void CustomizeEq_OnClick(object sender, RoutedEventArgs e)
    {
        var initial = string.Equals(SelectedTag(EqPresetBox), "custom", StringComparison.OrdinalIgnoreCase)
            ? _draftCustomEqBands
            : EqPresets.Get(SelectedTag(EqPresetBox) ?? "flat");

        var dlg = new EqualizerWindow(initial) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        _draftCustomEqBands = dlg.ResultBands.ToList();
        SelectComboByTag(EqPresetBox, "custom");
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
