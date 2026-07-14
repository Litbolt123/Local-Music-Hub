using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LocalMusicHub.Services;

/// <summary>Captures multimedia key app-commands even when the main window is unfocused.</summary>
public sealed class GlobalMediaKeyService : IDisposable
{
    private const int WmAppCommand = 0x0319;
    private const int AppCommandMediaPlayPause = 14;
    private const int AppCommandMediaNext = 11;
    private const int AppCommandMediaPrev = 12;
    private const int AppCommandMediaStop = 13;
    private const int AppCommandVolumeMute = 8;
    private const int AppCommandVolumeDown = 9;
    private const int AppCommandVolumeUp = 10;

    private readonly Window _window;
    private HwndSource? _source;
    private bool _disposed;

    public event Action? PlayPauseRequested;
    public event Action? NextRequested;
    public event Action? PreviousRequested;
    public event Action? StopRequested;
    public event Action? MuteRequested;
    public event Action? VolumeUpRequested;
    public event Action? VolumeDownRequested;

    public GlobalMediaKeyService(Window window)
    {
        _window = window;
        _window.SourceInitialized += Window_OnSourceInitialized;
        if (_window.IsLoaded)
            Attach();
    }

    private void Window_OnSourceInitialized(object? sender, EventArgs e) => Attach();

    private void Attach()
    {
        if (_source is not null)
            return;

        var helper = new WindowInteropHelper(_window);
        if (helper.Handle == IntPtr.Zero)
            return;

        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmAppCommand)
            return IntPtr.Zero;

        var cmd = ((int)((long)lParam >> 16)) & 0xFFFF;
        switch (cmd)
        {
            case AppCommandMediaPlayPause:
                PlayPauseRequested?.Invoke();
                handled = true;
                break;
            case AppCommandMediaNext:
                NextRequested?.Invoke();
                handled = true;
                break;
            case AppCommandMediaPrev:
                PreviousRequested?.Invoke();
                handled = true;
                break;
            case AppCommandMediaStop:
                StopRequested?.Invoke();
                handled = true;
                break;
            case AppCommandVolumeMute:
                MuteRequested?.Invoke();
                handled = true;
                break;
            case AppCommandVolumeUp:
                VolumeUpRequested?.Invoke();
                handled = true;
                break;
            case AppCommandVolumeDown:
                VolumeDownRequested?.Invoke();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _source?.RemoveHook(WndProc);
        _source = null;
        _disposed = true;
    }
}
