using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LocalMusicHub.Services;

/// <summary>Ramps volume per audio frame to avoid zipper noise when adjusting the slider.</summary>
public sealed class SmoothVolumeSampleProvider : ISampleProvider
{
    private const float RampMs = 5f;
    private const float SnapThreshold = 0.0002f;

    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly int _rampFramesDefault;

    private float _current = 1f;
    private float _target = 1f;
    private float _rampFrom;
    private float _rampTo = 1f;
    private int _rampFramesRemaining;
    private int _rampFramesTotal;

    public SmoothVolumeSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = Math.Max(1, source.WaveFormat.Channels);
        _rampFramesDefault = Math.Max(1, (int)(source.WaveFormat.SampleRate * RampMs / 1000.0));
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public float Volume
    {
        get => _target;
        set => _target = Math.Clamp(value, 0f, 8f);
    }

    public void RampTo(float target, int rampFrames)
    {
        _target = Math.Clamp(target, 0f, 8f);
        if (rampFrames <= 0 || Math.Abs(_target - _current) < SnapThreshold)
        {
            _current = _target;
            _rampTo = _target;
            _rampFramesRemaining = 0;
            return;
        }

        _rampFrom = _current;
        _rampTo = _target;
        _rampFramesTotal = Math.Max(1, rampFrames);
        _rampFramesRemaining = _rampFramesTotal;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        if (read == 0)
            return 0;

        var frames = read / _channels;
        for (var frame = 0; frame < frames; frame++)
        {
            if (Math.Abs(_rampTo - _target) > SnapThreshold)
                BeginRampToward(_target);

            if (_rampFramesRemaining > 0)
            {
                var progressed = _rampFramesTotal - _rampFramesRemaining + 1;
                var t = (float)progressed / _rampFramesTotal;
                _current = _rampFrom + (_rampTo - _rampFrom) * t;
                _rampFramesRemaining--;
                if (_rampFramesRemaining == 0)
                    _current = _rampTo;
            }
            else if (Math.Abs(_current - _target) > SnapThreshold)
            {
                _current = _target;
            }

            if (Math.Abs(_current - 1f) <= SnapThreshold)
                continue;

            var sampleOffset = offset + frame * _channels;
            for (var ch = 0; ch < _channels; ch++)
                buffer[sampleOffset + ch] *= _current;
        }

        return read;
    }

    private void BeginRampToward(float target)
    {
        if (Math.Abs(target - _current) < SnapThreshold)
        {
            _current = target;
            _rampTo = target;
            _rampFramesRemaining = 0;
            return;
        }

        _rampFrom = _current;
        _rampTo = target;
        _rampFramesTotal = _rampFramesDefault;
        _rampFramesRemaining = _rampFramesTotal;
    }
}
