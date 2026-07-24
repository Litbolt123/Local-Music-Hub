using System.Diagnostics;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace LocalMusicHub.Services;

/// <summary>
/// WAID-style: download GitHub Setup EXE to temp, launch it, fully quit (not tray).
/// </summary>
public static class InstallerUpgrade
{
    public static async Task RunAsync(
        Window owner,
        string? installerUrl,
        string? latestVersion,
        Action<string>? setStatus = null)
    {
        var url = installerUrl;
        var ver = latestVersion;

        if (string.IsNullOrWhiteSpace(url))
        {
            setStatus?.Invoke("Checking for download link…");
            var r = await UpdateCheckService.CheckLatestReleaseAsync().ConfigureAwait(true);
            if (!r.Success || !r.IsNewerThanCurrent || string.IsNullOrWhiteSpace(r.InstallerDownloadUrl))
            {
                MessageBox.Show(owner,
                    "No installer download is available right now.\n\n" +
                    "Use “Open releases page” and download LocalMusicHub-Setup-….exe manually, " +
                    "then quit Local Music Hub before running it.",
                    "Update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            url = r.InstallerDownloadUrl;
            ver = r.LatestVersion;
            UpdateAvailabilityCache.Set(r.LatestVersion, r.InstallerDownloadUrl);
        }

        var confirm = MessageBox.Show(owner,
            "Local Music Hub will STOP completely (it will not stay in the system tray).\n\n" +
            "The installer will download to your Temp folder, then setup will start.\n\n" +
            "If Windows SmartScreen appears, use More info → Run anyway only if you trust this GitHub release.\n\n" +
            "Continue?",
            "Download and install update?",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK)
            return;

        setStatus?.Invoke("Downloading installer (this may take a minute)…");
        var (path, error) = await UpdateCheckService.DownloadInstallerToTempAsync(url!, ver)
            .ConfigureAwait(true);
        if (path is null)
        {
            MessageBox.Show(owner, error ?? "Download failed.", "Update",
                MessageBoxButton.OK, MessageBoxImage.Error);
            setStatus?.Invoke(error ?? "Download failed.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner,
                "Saved the installer but could not start it:\n" + ex.Message +
                "\n\nYou can run this file yourself:\n" + path,
                "Update",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            setStatus?.Invoke("Installer saved but could not start.");
            return;
        }

        setStatus?.Invoke("Installer started — quitting…");
        if (Application.Current is App app)
            app.ExitForInstallerUpgrade();
        else
            Environment.Exit(0);
    }
}
