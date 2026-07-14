using System.Windows;
using LocalMusicHub.Services;
using Application = System.Windows.Application;

namespace LocalMusicHub;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    public static AppSettings Settings { get; private set; } = new();

    /// <summary>Path passed via --import on startup (from YouTube Downloader).</summary>
    public static string? PendingImportPath { get; private set; }

    /// <summary>When true, <see cref="PendingImportPath"/> is an album folder.</summary>
    public static bool PendingImportFolder { get; private set; }

    public static string VersionDisplay =>
        UpdateCheckService.CurrentAssemblyVersion.ToString(3);

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!SingleInstanceService.TryBecomePrimaryInstance(out _singleInstanceMutex))
        {
            SingleInstanceService.NotifyPrimaryInstance(e.Args);
            Shutdown();
            return;
        }

        Settings = AppSettingsService.Load();
        Settings.StartWithWindows = AutoStartService.IsEnabled();
        if (Settings.StartWithWindows)
            Settings.MinimizeToTray = true;
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                var logPath = Path.Combine(AppPaths.DataDirectory, "startup-crash.log");
                Directory.CreateDirectory(AppPaths.DataDirectory);
                File.AppendAllText(logPath,
                    $"[{DateTime.UtcNow:O}] {args.Exception}\r\n");
            }
            catch
            {
                /* ignore */
            }

            System.Windows.MessageBox.Show(
                $"Local Music Hub failed to start:\n\n{args.Exception.Message}",
                "Local Music Hub",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(-1);
        };
        PendingImportPath = ParseImportPath(e.Args);
        PendingImportFolder = e.Args.Any(a =>
            string.Equals(a, "--import-folder", StringComparison.OrdinalIgnoreCase));
        HubTheme.ApplyFromSettings();
        base.OnStartup(e);

        if (Settings.AutoCheckUpdates)
            _ = CheckForUpdatesOnStartupAsync();
    }

    private static async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var r = await UpdateCheckService.CheckLatestReleaseAsync().ConfigureAwait(false);
            if (!r.Success || !r.IsNewerThanCurrent || string.IsNullOrWhiteSpace(r.LatestVersion))
                return;

            if (string.Equals(Settings.DismissedUpdateVersion, r.LatestVersion, StringComparison.OrdinalIgnoreCase))
                return;

            await Current.Dispatcher.InvokeAsync(() =>
            {
                if (Current.MainWindow is MainWindow mw)
                    mw.ShowUpdateAvailable(r);
            });
        }
        catch
        {
            /* best-effort */
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
