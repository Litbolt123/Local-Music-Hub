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

    public static string VersionDisplay =>
        UpdateCheckService.CurrentAssemblyVersion.ToString(3);

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

    public static void ReplaceSettings(AppSettings settings) => Settings = settings;

    public static void SaveSettings()
    {
        AppSettingsService.Save(Settings);
    }
}
