using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace LocalMusicHub.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _ownedIcon;
    private MainWindow? _window;
    private bool _disposed;

    public event Action? PlayPauseRequested;
    public event Action? NextRequested;
    public event Action? PreviousRequested;

    public TrayIconService()
    {
        _ownedIcon = TrayIconAssets.CreateIcon();
        _icon = new NotifyIcon
        {
            Text = "Local Music Hub",
            Icon = _ownedIcon,
            Visible = false,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Local Music Hub", null, (_, _) => ShowWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Play / Pause", null, (_, _) => PlayPauseRequested?.Invoke());
        menu.Items.Add("Next track", null, (_, _) => NextRequested?.Invoke());
        menu.Items.Add("Previous track", null, (_, _) => PreviousRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open library folder", null, (_, _) => OpenPrimaryLibraryFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitApp());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => ShowWindow();
    }

    public void Attach(MainWindow window) => _window = window;

    public void ShowTrayIcon() => _icon.Visible = true;

    public void HideTrayIcon() => _icon.Visible = false;

    public void MinimizeToTray()
    {
        ShowTrayIcon();
        _window?.Hide();
    }

    public void ShowMainWindow()
    {
        if (_window is null)
            return;

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void UpdateTooltip(string? title, string? artist)
    {
        try
        {
            var tip = string.IsNullOrWhiteSpace(title)
                ? "Local Music Hub"
                : string.IsNullOrWhiteSpace(artist)
                    ? title!
                    : $"{title} — {artist}";
            if (tip.Length > 63)
                tip = tip[..60] + "…";
            _icon.Text = tip;
        }
        catch
        {
            /* ignore */
        }
    }

    public void NotifyTrackChanged(string title, string artist)
    {
        if (!App.Settings.NotifyOnTrackChange || !_icon.Visible)
            return;

        try
        {
            _icon.BalloonTipTitle = string.IsNullOrWhiteSpace(title) ? "Now playing" : title;
            _icon.BalloonTipText = string.IsNullOrWhiteSpace(artist) ? "Local Music Hub" : artist;
            _icon.ShowBalloonTip(2500);
        }
        catch
        {
            /* ignore */
        }
    }

    public void ShowUpdateAvailableBalloon(string version, string downloadUrl)
    {
        if (!_icon.Visible)
            return;

        try
        {
            _pendingUpdateBalloonUrl = downloadUrl;
            _icon.BalloonTipClicked -= UpdateBalloon_OnClicked;
            _icon.BalloonTipClicked += UpdateBalloon_OnClicked;
            _icon.ShowBalloonTip(
                14000,
                $"Local Music Hub {version} is available",
                "Click here to open the download. Quit this app before running the installer.",
                ToolTipIcon.Info);
        }
        catch
        {
            /* ignore */
        }
    }

    private string? _pendingUpdateBalloonUrl;

    private void UpdateBalloon_OnClicked(object? sender, EventArgs e)
    {
        var url = _pendingUpdateBalloonUrl;
        _pendingUpdateBalloonUrl = null;
        if (string.IsNullOrWhiteSpace(url))
            url = UpdateCheckService.ReleasesPageUrl;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            /* ignore */
        }
    }

    private void ShowWindow() => ShowMainWindow();

    private static void OpenPrimaryLibraryFolder()
    {
        var folder = App.Settings.LibraryFolders.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            folder = AppPaths.DefaultMusicFolder;

        try
        {
            if (Directory.Exists(folder))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true,
                });
        }
        catch
        {
            /* ignore */
        }
    }

    private void ExitApp()
    {
        _window?.RequestForceClose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _icon.Visible = false;
        _icon.Dispose();
        _ownedIcon.Dispose();
        _disposed = true;
    }
}
