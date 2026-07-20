using NAudio.Wave;

namespace LocalMusicHub.Services;

/// <summary>
/// Changes playback speed by resampling (pitch shifts with speed — v1 behavior).
/// </summary>
public sealed class SpeedSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private double _sourceIndex;

    public SpeedSampleProvider(ISampleProvider source) => _source = source;

    public float Speed { get; set; } = 1f;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public void ResetInterpolation() => _sourceIndex = 0;

    public int Read(float[] buffer, int offset, int count)
    {
        var speed = Math.Clamp(Speed, 0.5f, 2f);
        if (Math.Abs(speed - 1f) < 0.0001f)
            return _source.Read(buffer, offset, count);

        var channels = WaveFormat.Channels;
        var outFrames = count / channels;
        if (outFrames <= 0)
            return 0;

        var maxInFrames = (int)Math.Ceiling((outFrames - 1) * speed + 2);
        var temp = new float[maxInFrames * channels];
        var read = _source.Read(temp, 0, temp.Length);
        var inFrames = read / channels;
        if (inFrames <= 0)
            return 0;

        for (var frame = 0; frame < outFrames; frame++)
        {
            var srcPos = _sourceIndex + frame * speed;
            var idx = (int)srcPos;
            var frac = (float)(srcPos - idx);
            if (idx >= inFrames - 1)
            {
                if (idx >= inFrames)
                {
                    Array.Clear(buffer, offset + frame * channels, (outFrames - frame) * channels);
                    return frame * channels;
                }

                frac = 0f;
            }

            for (var ch = 0; ch < channels; ch++)
            {
                var a = temp[idx * channels + ch];
                var b = temp[Math.Min(idx + 1, inFrames - 1) * channels + ch];
                buffer[offset + frame * channels + ch] = a + (b - a) * frac;
            }
        }

        _sourceIndex += outFrames * speed;
        while (_sourceIndex >= inFrames - 1)
            _sourceIndex -= inFrames - 1;

        return outFrames * channels;
    }
}
