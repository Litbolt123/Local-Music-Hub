using System.Windows.Threading;

namespace LocalMusicHub.Services;

public enum SleepTimerMode
{
    Off,
    Duration,
    EndOfTrack,
    EndOfQueue,
}

public sealed class SleepTimerService : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Action<double> _setVolume;
    private readonly Action _stopPlayback;
    private DateTime? _endsAtUtc;
    private double _volumeAtStart = 1;
    private bool _fading;
    private bool _disposed;
    private SleepTimerMode _mode = SleepTimerMode.Off;

    public event EventHandler? Changed;

    public SleepTimerService(Action<double> setVolume, Action stopPlayback)
    {
        _setVolume = setVolume;
        _stopPlayback = stopPlayback;
        _timer.Tick += Timer_OnTick;
    }

    public SleepTimerMode Mode => _mode;
    public bool IsActive => _mode != SleepTimerMode.Off;
    public TimeSpan? Remaining =>
        _endsAtUtc is null ? null : TimeSpan.FromSeconds(Math.Max(0, (_endsAtUtc.Value - DateTime.UtcNow).TotalSeconds));

    public void Start(TimeSpan duration, double currentVolume)
    {
        if (duration <= TimeSpan.Zero)
        {
            Cancel();
            return;
        }

        _volumeAtStart = Math.Clamp(currentVolume, 0.05, 1);
        _mode = SleepTimerMode.Duration;
        _endsAtUtc = DateTime.UtcNow + duration;
        _fading = false;
        _timer.Start();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void StartEndOfTrack(double currentVolume)
    {
        _volumeAtStart = Math.Clamp(currentVolume, 0.05, 1);
        _mode = SleepTimerMode.EndOfTrack;
        _endsAtUtc = null;
        _fading = false;
        _timer.Stop();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void StartEndOfQueue(double currentVolume)
    {
        _volumeAtStart = Math.Clamp(currentVolume, 0.05, 1);
        _mode = SleepTimerMode.EndOfQueue;
        _endsAtUtc = null;
        _fading = false;
        _timer.Stop();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Called by playback when a track naturally ends — returns true if playback should stop instead of advancing.</summary>
    public bool ShouldStopInsteadOfAdvancing(bool isLastInQueue)
    {
        if (_mode == SleepTimerMode.EndOfTrack)
        {
            Finish();
            return true;
        }

        if (_mode == SleepTimerMode.EndOfQueue && isLastInQueue)
        {
            Finish();
            return true;
        }

        return false;
    }

    public void Cancel()
    {
        _mode = SleepTimerMode.Off;
        _endsAtUtc = null;
        _fading = false;
        _timer.Stop();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Timer_OnTick(object? sender, EventArgs e)
    {
        if (_mode != SleepTimerMode.Duration || _endsAtUtc is null)
            return;

        var remaining = _endsAtUtc.Value - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            Finish();
            return;
        }

        if (remaining <= TimeSpan.FromSeconds(15))
        {
            _fading = true;
            var t = remaining.TotalSeconds / 15.0;
            _setVolume(_volumeAtStart * Math.Clamp(t, 0, 1));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Finish()
    {
        _mode = SleepTimerMode.Off;
        _endsAtUtc = null;
        _fading = false;
        _timer.Stop();
        _stopPlayback();
        _setVolume(_volumeAtStart);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string StatusLabel => _mode switch
    {
        SleepTimerMode.Duration when Remaining is { } rem =>
            $"Sleep {rem.Minutes}:{rem.Seconds:D2}" + (_fading ? " · fading" : ""),
        SleepTimerMode.EndOfTrack => "Sleep · track",
        SleepTimerMode.EndOfQueue => "Sleep · queue",
        _ => "Sleep",
    };

    public void Dispose()
    {
        if (_disposed)
            return;
        _timer.Stop();
        _disposed = true;
    }
}
