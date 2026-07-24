using System.Globalization;
using System.Windows;
using LocalMusicHub.Services;
using Application = System.Windows.Application;

namespace LocalMusicHub;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private static bool _updateTrayNotifiedThisSession;

    public static AppSettings Settings { get; private set; } = new();

    /// <summary>Path passed via --import on startup (from YouTube Downloader).</summary>
    public static string? PendingImportPath { get; private set; }

    /// <summary>When true, <see cref="PendingImportPath"/> is an album folder.</summary>
    public static bool PendingImportFolder { get; private set; }

    /// <summary>Playlist name from --playlist (Harbor greeting / CLI).</summary>
    public static string? PendingPlaylistName { get; private set; }

    /// <summary>Volume 0–1 from --volume (Harbor greeting / CLI).</summary>
    public static double? PendingVolume { get; private set; }

    public static void ClearPendingPlaylistName() => PendingPlaylistName = null;

    public static void ClearPendingVolume() => PendingVolume = null;

    public static string VersionDisplay =>
        UpdateCheckService.CurrentAssemblyVersion.ToString(3);

    /// <summary>When true, <see cref="MainWindow"/> must allow close so the process can exit for an installer upgrade.</summary>
    public bool BypassMainWindowCloseCancel { get; set; }

    /// <summary>Fully quit after launching the downloaded setup (user already confirmed). Not tray-minimize.</summary>
    public void ExitForInstallerUpgrade()
    {
        BypassMainWindowCloseCancel = true;

        // Close modal Settings / other owned windows so Shutdown is not blocked.
        try
        {
            foreach (Window w in Windows.Cast<Window>().ToList())
            {
                if (ReferenceEquals(w, MainWindow))
                    continue;
                try { w.Close(); } catch { /* ignore */ }
            }
        }
        catch
        {
            /* ignore */
        }

        if (MainWindow is MainWindow mw)
            mw.RequestForceClose();
        Shutdown(0);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupProfiler.Mark("app.on_startup");

        if (!SingleInstanceService.TryBecomePrimaryInstance(out _singleInstanceMutex))
        {
            SingleInstanceService.NotifyPrimaryInstance(e.Args);
            Shutdown();
            return;
        }

        StartupProfiler.Mark("app.single_instance_ok");

        Settings = AppSettingsService.Load();
        AppSettingsService.EnsureLibraryIngestToken(Settings);
        StartupProfiler.Configure(Settings.LogStartupTiming);
        StartupProfiler.Mark("app.settings_loaded");

        Settings.StartWithWindows = AutoStartService.IsEnabled();
        if (Settings.StartWithWindows)
            Settings.MinimizeToTray = true;
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                var logPath = Path.Combine(AppPaths.DataDirectory, "crash.log");
                Directory.CreateDirectory(AppPaths.DataDirectory);
                File.AppendAllText(logPath,
                    $"[{DateTime.UtcNow:O}] {args.Exception}\r\n");
            }
            catch
            {
                /* ignore */
            }

            System.Windows.MessageBox.Show(
                $"Local Music Hub encountered an error:\n\n{args.Exception.Message}",
                "Local Music Hub",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
        PendingImportPath = ParseImportPath(e.Args);
        PendingImportFolder = e.Args.Any(a =>
            string.Equals(a, "--import-folder", StringComparison.OrdinalIgnoreCase));
        PendingPlaylistName = ParsePlaylistName(e.Args);
        PendingVolume = ParseVolume(e.Args);
        HubTheme.ApplyFromSettings();
        StartupProfiler.Mark("app.theme_applied");
        base.OnStartup(e);
        StartupProfiler.Mark("app.mainwindow_created");

        if (Settings.AutoCheckUpdates)
            SchedulePostStartupUpdateCheck();
    }

    private static void SchedulePostStartupUpdateCheck()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(12)).ConfigureAwait(false);
                await CheckForUpdatesOnStartupAsync().ConfigureAwait(false);
            }
            catch
            {
                /* best-effort */
            }
        });
    }

    private static async Task CheckForUpdatesOnStartupAsync()
    {
        var r = await UpdateCheckService.CheckLatestReleaseAsync().ConfigureAwait(false);
        if (r.Success)
        {
            Settings.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            SaveSettings();
        }

        if (!r.Success || r.NoPublishedReleases || !r.IsNewerThanCurrent ||
            string.IsNullOrWhiteSpace(r.LatestVersion))
        {
            UpdateAvailabilityCache.Clear();
            return;
        }

        if (string.Equals(Settings.DismissedUpdateVersion, r.LatestVersion, StringComparison.OrdinalIgnoreCase))
        {
            UpdateAvailabilityCache.Clear();
            return;
        }

        UpdateAvailabilityCache.Set(r.LatestVersion, r.InstallerDownloadUrl);

        var showTray = Settings.NotifyTrayOnUpdate && !_updateTrayNotifiedThisSession;
        if (showTray)
            _updateTrayNotifiedThisSession = true;

        await Current.Dispatcher.InvokeAsync(() =>
        {
            if (Current.MainWindow is MainWindow mw)
                mw.ApplyStartupUpdate(r, showTray);
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { SaveSettings(); }
        catch { /* ignore */ }

        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }

    private static string? ParseImportPath(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--import", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1].Trim('"');

            if (args[i].StartsWith("--import=", StringComparison.OrdinalIgnoreCase))
                return args[i]["--import=".Length..].Trim('"');
        }

        return null;
    }

    private static string? ParsePlaylistName(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--playlist", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1].Trim().Trim('"');

            if (args[i].StartsWith("--playlist=", StringComparison.OrdinalIgnoreCase))
                return args[i]["--playlist=".Length..].Trim().Trim('"');
        }

        return null;
    }

    private static double? ParseVolume(string[] args)
    {
        string? raw = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--volume", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                raw = args[i + 1].Trim().Trim('"');
                break;
            }

            if (args[i].StartsWith("--volume=", StringComparison.OrdinalIgnoreCase))
            {
                raw = args[i]["--volume=".Length..].Trim().Trim('"');
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(raw) ||
            !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return null;

        return VolumeRequestService.Clamp01(v);
    }

    public static void ReplaceSettings(AppSettings settings) => Settings = settings;

    public static void SaveSettings()
    {
        AppSettingsService.Save(Settings);
    }
}
