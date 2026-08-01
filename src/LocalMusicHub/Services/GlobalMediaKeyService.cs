using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LocalMusicHub.Services;

/// <summary>
/// Global multimedia keys via RegisterHotKey (works when LMH is in the tray / unfocused).
/// Also handles WM_APPCOMMAND when focused (volume keys + fallback if hotkey registration fails).
/// </summary>
public sealed class GlobalMediaKeyService : IDisposable
{
    private const int WmAppCommand = 0x0319;
    private const int WmHotKey = 0x0312;

    private const int AppCommandMediaPlayPause = 14;
    private const int AppCommandMediaNext = 11;
    private const int AppCommandMediaPrev = 12;
    private const int AppCommandMediaStop = 13;
    private const int AppCommandVolumeMute = 8;
    private const int AppCommandVolumeDown = 9;
    private const int AppCommandVolumeUp = 10;

    private const uint ModNoRepeat = 0x4000;
    private const uint VkMediaNextTrack = 0xB0;
    private const uint VkMediaPrevTrack = 0xB1;
    private const uint VkMediaStop = 0xB2;
    private const uint VkMediaPlayPause = 0xB3;

    private const int HotIdPlayPause = 0x4C4D4801;
    private const int HotIdNext = 0x4C4D4802;
    private const int HotIdPrev = 0x4C4D4803;
    private const int HotIdStop = 0x4C4D4804;

    private readonly Window _window;
    private readonly HashSet<int> _registeredIds = [];
    private HwndSource? _source;
    private IntPtr _hwnd;
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
        helper.EnsureHandle();
        _hwnd = helper.Handle;
        if (_hwnd == IntPtr.Zero)
            return;

        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
        RegisterMediaHotKeys();
    }

    private void RegisterMediaHotKeys()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        // Global hotkeys so Play/Pause works while LMH is minimized to tray or another
        // window is focused. WM_APPCOMMAND alone only reaches the foreground HWND.
        TryRegister(HotIdPlayPause, VkMediaPlayPause);
        TryRegister(HotIdNext, VkMediaNextTrack);
        TryRegister(HotIdPrev, VkMediaPrevTrack);
        TryRegister(HotIdStop, VkMediaStop);
    }

    private void TryRegister(int id, uint vk)
    {
        if (RegisterHotKey(_hwnd, id, ModNoRepeat, vk))
            _registeredIds.Add(id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey)
        {
            switch (wParam.ToInt32())
            {
                case HotIdPlayPause:
                    PlayPauseRequested?.Invoke();
                    handled = true;
                    break;
                case HotIdNext:
                    NextRequested?.Invoke();
                    handled = true;
                    break;
                case HotIdPrev:
                    PreviousRequested?.Invoke();
                    handled = true;
                    break;
                case HotIdStop:
                    StopRequested?.Invoke();
                    handled = true;
                    break;
            }

            return IntPtr.Zero;
        }

        if (msg != WmAppCommand)
            return IntPtr.Zero;

        var cmd = ((int)((long)lParam >> 16)) & 0xFFFF;
        switch (cmd)
        {
            case AppCommandMediaPlayPause:
                // Avoid double-fire when both hotkey and APPCOMMAND arrive while focused.
                if (!_registeredIds.Contains(HotIdPlayPause))
                    PlayPauseRequested?.Invoke();
                handled = true;
                break;
            case AppCommandMediaNext:
                if (!_registeredIds.Contains(HotIdNext))
                    NextRequested?.Invoke();
                handled = true;
                break;
            case AppCommandMediaPrev:
                if (!_registeredIds.Contains(HotIdPrev))
                    PreviousRequested?.Invoke();
                handled = true;
                break;
            case AppCommandMediaStop:
                if (!_registeredIds.Contains(HotIdStop))
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

        if (_hwnd != IntPtr.Zero)
        {
            foreach (var id in _registeredIds)
                UnregisterHotKey(_hwnd, id);
            _registeredIds.Clear();
        }

        _source?.RemoveHook(WndProc);
        _source = null;
        _disposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
