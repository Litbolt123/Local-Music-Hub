using NAudio.Wave;

namespace LocalMusicHub.Services;

public sealed class CrossfadeHandoffEventArgs : EventArgs
{
    public required HubAudioReader Reader { get; init; }
    public required ISampleProvider SampleProvider { get; init; }
}

public sealed class CrossfadeSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _crossfadeSeconds;
    private HubAudioReader? _fadeInReader;
    private ISampleProvider? _fadeInSamples;
    private int _fadeSamplesRemaining;
    private int _fadeSamplesTotal;
    private Func<TimeSpan>? _getRemaining;
    private Func<HubAudioReader?>? _acquireNextReader;
    private Action<HubAudioReader, ISampleProvider>? _onIncomingReserved;
    private bool _handedOff;
    private CrossfadeHandoffEventArgs? _pendingHandoff;

    public CrossfadeSampleProvider(ISampleProvider source, int crossfadeSeconds, int sampleRate)
    {
        _source = source;
        WaveFormat = source.WaveFormat;
        _crossfadeSeconds = Math.Clamp(crossfadeSeconds, 1, 30);
    }

    public WaveFormat WaveFormat { get; }

    public event EventHandler<CrossfadeHandoffEventArgs>? CrossfadeCompleted;
    public event EventHandler? CrossfadeStarted;

    public bool IsFading => _fadeInReader is not null && _fadeSamplesRemaining > 0;
    public bool HasPendingHandoff => _pendingHandoff is not null;

    public void ConfigureTransition(
        Func<TimeSpan> getRemaining,
        Func<HubAudioReader?> acquireNextReader,
        Action<HubAudioReader, ISampleProvider>? onIncomingReserved = null)
    {
        _getRemaining = getRemaining;
        _acquireNextReader = acquireNextReader;
        _onIncomingReserved = onIncomingReserved;
    }

    public void BeginCrossfade(HubAudioReader nextReader)
    {
        var remaining = _getRemaining?.Invoke() ?? TimeSpan.FromSeconds(_crossfadeSeconds);
        var fadeSeconds = Math.Min(_crossfadeSeconds, Math.Max(remaining.TotalSeconds, 0));
        var fadeSamples = SecondsToSamples(fadeSeconds);
        BeginCrossfade(nextReader, fadeSamples);
    }

    private void BeginCrossfade(HubAudioReader nextReader, int fadeSamples)
    {
        if (!_handedOff)
            _fadeInReader?.Dispose();
        _handedOff = false;
        _fadeInReader = nextReader;
        _fadeInSamples = nextReader;
        _fadeSamplesTotal = Math.Max(WaveFormat.Channels, fadeSamples);
        _fadeSamplesRemaining = _fadeSamplesTotal;
        _onIncomingReserved?.Invoke(nextReader, _fadeInSamples);
        CrossfadeStarted?.Invoke(this, EventArgs.Empty);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        FlushPendingHandoff();
        TryBeginCrossfadeNearEnd();

        var read = _source.Read(buffer, offset, count);
        if (_fadeInReader is null || _fadeSamplesRemaining <= 0 || _fadeInSamples is null)
            return read;

        if (read < count)
        {
            if (read > 0)
                Array.Clear(buffer, offset + read, count - read);
            else
                Array.Clear(buffer, offset, count);
            read = count;
        }

        var fadeBuffer = new float[read];
        var fadeRead = _fadeInSamples.Read(fadeBuffer, 0, read);
        for (var i = 0; i < read; i++)
        {
            var incoming = i < fadeRead ? fadeBuffer[i] : 0f;
            MixSample(buffer, offset, incoming, i);
        }

        CompleteFadeIfDone();
        return read;
    }

    private void TryBeginCrossfadeNearEnd()
    {
        if (IsFading || _pendingHandoff is not null || _getRemaining is null || _acquireNextReader is null)
            return;

        var remaining = _getRemaining();
        if (remaining <= TimeSpan.Zero || remaining > TimeSpan.FromSeconds(_crossfadeSeconds))
            return;

        var next = _acquireNextReader();
        if (next is null)
            return;

        var fadeSamples = SecondsToSamples(Math.Min(_crossfadeSeconds, remaining.TotalSeconds));
        BeginCrossfade(next, fadeSamples);
    }

    private int SecondsToSamples(double seconds) =>
        (int)Math.Ceiling(seconds * WaveFormat.SampleRate * WaveFormat.Channels);

    private void MixSample(float[] buffer, int offset, float incoming, int index)
    {
        var outGain = OutgoingGain();
        var inGain = IncomingGain();
        buffer[offset + index] = buffer[offset + index] * outGain + incoming * inGain;
        _fadeSamplesRemaining--;
    }

    private float OutgoingGain()
    {
        if (_fadeSamplesRemaining <= WaveFormat.Channels)
            return 0f;

        var progress = FadeProgress();
        return MathF.Sin((1f - progress) * MathF.PI * 0.5f);
    }

    private float IncomingGain()
    {
        if (_fadeSamplesRemaining <= WaveFormat.Channels)
            return 1f;

        return MathF.Sin(FadeProgress() * MathF.PI * 0.5f);
    }

    private float FadeProgress() =>
        Math.Clamp(1f - (_fadeSamplesRemaining / (float)_fadeSamplesTotal), 0f, 1f);

    private void CompleteFadeIfDone()
    {
        if (_fadeSamplesRemaining > 0 || _fadeInReader is null || _fadeInSamples is null)
            return;

        _pendingHandoff = new CrossfadeHandoffEventArgs
        {
            Reader = _fadeInReader,
            SampleProvider = _fadeInSamples,
        };
        _handedOff = true;
        _fadeInReader = null;
        _fadeInSamples = null;
    }

    private void FlushPendingHandoff()
    {
        if (_pendingHandoff is null)
            return;

        var args = _pendingHandoff;
        _pendingHandoff = null;
        CrossfadeCompleted?.Invoke(this, args);
    }

    public void Clear()
    {
        // Readers are owned by GaplessSampleProvider (incoming) or PlaybackService (outgoing);
        // do not dispose here — gapless.Clear() runs immediately after in StopInternal.
        _pendingHandoff = null;
        _fadeInReader = null;
        _fadeInSamples = null;
        _fadeSamplesRemaining = 0;
        _fadeSamplesTotal = 0;
        _handedOff = false;
    }
}
