using System.Threading;
using NAudio.Wave;

namespace LocalMusicHub.Services;

/// <summary>
/// ReplayGain (+ optional crossfade RG ramp) and user/master volume in the sample chain.
/// Master volume uses a ~1.5 ms per-sample linear dezipper (not an audible fade) so slider
/// moves stay instant without clicks/ticks or Wasapi endpoint-volume buzz.
/// Ramp state is owned by the audio thread only.
/// </summary>
public sealed class SmoothVolumeSampleProvider : ISampleProvider
{
    private const float SnapThreshold = 0.00015f;
    /// <summary>Inaudible as a fade; long enough to remove zipper ticks.</summary>
    private const float MasterDezipperMs = 1.5f;

    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly int _masterRampFramesDefault;

    private float _rgCurrent = 1f;
    private float _rgTarget = 1f;
    private float _rampFrom;
    private float _rampTo = 1f;
    private int _rampFramesRemaining;
    private int _rampFramesTotal;

    private float _masterCurrent = 1f;
    private float _masterTarget = 1f;
    private float _masterRampFrom = 1f;
    private float _masterRampTo = 1f;
    private int _masterRampRemaining;
    private int _masterRampTotal;

    public SmoothVolumeSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = Math.Max(1, source.WaveFormat.Channels);
        _masterRampFramesDefault = Math.Max(1, (int)(source.WaveFormat.SampleRate * MasterDezipperMs / 1000.0));
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>ReplayGain linear gain (instant). Use <see cref="RampTo"/> during crossfade.</summary>
    public float Volume
    {
        get => Volatile.Read(ref _rgTarget);
        set
        {
            var v = Math.Clamp(value, 0f, 8f);
            Volatile.Write(ref _rgTarget, v);
            Volatile.Write(ref _rgCurrent, v);
            _rampFramesRemaining = 0;
        }
    }

    /// <summary>User/master volume 0–1. Feels instant; micro-dezippered on the audio thread.</summary>
    public float MasterVolume
    {
        get => Volatile.Read(ref _masterTarget);
        set => Volatile.Write(ref _masterTarget, Math.Clamp(value, 0f, 1f));
    }

    /// <summary>Linear ReplayGain ramp over <paramref name="rampFrames"/> (crossfade only).</summary>
    public void RampTo(float target, int rampFrames)
    {
        var v = Math.Clamp(target, 0f, 8f);
        Volatile.Write(ref _rgTarget, v);
        var current = Volatile.Read(ref _rgCurrent);
        if (rampFrames <= 0 || Math.Abs(v - current) < SnapThreshold)
        {
            Volatile.Write(ref _rgCurrent, v);
            _rampTo = v;
            _rampFramesRemaining = 0;
            return;
        }

        _rampFrom = current;
        _rampTo = v;
        _rampFramesTotal = Math.Max(1, rampFrames);
        _rampFramesRemaining = _rampFramesTotal;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        if (read == 0)
            return 0;

        var rgTarget = Volatile.Read(ref _rgTarget);
        var masterTarget = Volatile.Read(ref _masterTarget);
        var frames = read / _channels;

        for (var frame = 0; frame < frames; frame++)
        {
            // --- ReplayGain ---
            float rg;
            if (_rampFramesRemaining > 0)
            {
                if (Math.Abs(_rampTo - rgTarget) > SnapThreshold)
                {
                    _rampFramesRemaining = 0;
                    _rgCurrent = rgTarget;
                    rg = rgTarget;
                }
                else
                {
                    var progressed = _rampFramesTotal - _rampFramesRemaining + 1;
                    var t = (float)progressed / _rampFramesTotal;
                    rg = _rampFrom + (_rampTo - _rampFrom) * t;
                    _rampFramesRemaining--;
                    if (_rampFramesRemaining == 0)
                        rg = _rampTo;
                    _rgCurrent = rg;
                }
            }
            else
            {
                rg = _rgCurrent;
            }

            // --- Master: start/retarget micro-ramp when target moves ---
            if (Math.Abs(_masterRampTo - masterTarget) > SnapThreshold ||
                (_masterRampRemaining <= 0 && Math.Abs(_masterCurrent - masterTarget) > SnapThreshold))
            {
                _masterRampFrom = _masterCurrent;
                _masterRampTo = masterTarget;
                _masterRampTotal = _masterRampFramesDefault;
                _masterRampRemaining = _masterRampTotal;
            }

            float master;
            if (_masterRampRemaining > 0)
            {
                var progressed = _masterRampTotal - _masterRampRemaining + 1;
                var t = (float)progressed / _masterRampTotal;
                master = _masterRampFrom + (_masterRampTo - _masterRampFrom) * t;
                _masterRampRemaining--;
                if (_masterRampRemaining == 0)
                    master = _masterRampTo;
                _masterCurrent = master;
            }
            else
            {
                master = _masterCurrent;
            }

            var gain = rg * master;
            if (Math.Abs(gain - 1f) <= SnapThreshold)
                continue;

            var sampleOffset = offset + frame * _channels;
            for (var ch = 0; ch < _channels; ch++)
                buffer[sampleOffset + ch] *= gain;
        }

        // Keep volatile copies coherent for UI / other readers.
        Volatile.Write(ref _rgCurrent, _rgCurrent);
        Volatile.Write(ref _masterCurrent, _masterCurrent);

        return read;
    }
}
