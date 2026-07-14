using NAudio.Wave;

namespace LocalMusicHub.Services;

public sealed class CrossfadeSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private AudioFileReader? _fadeInReader;
    private int _fadeSamplesRemaining;
    private readonly int _fadeSamplesTotal;

    public CrossfadeSampleProvider(ISampleProvider source, int crossfadeSeconds, int sampleRate)
    {
        _source = source;
        WaveFormat = source.WaveFormat;
        _fadeSamplesTotal = Math.Max(sampleRate, crossfadeSeconds * sampleRate);
        _fadeSamplesRemaining = 0;
    }

    public WaveFormat WaveFormat { get; }

    public void BeginCrossfade(AudioFileReader nextReader)
    {
        _fadeInReader?.Dispose();
        _fadeInReader = nextReader;
        _fadeSamplesRemaining = _fadeSamplesTotal;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        if (_fadeInReader is null || _fadeSamplesRemaining <= 0)
            return read;

        var fadeBuffer = new float[read];
        var fadeRead = _fadeInReader.ToSampleProvider().Read(fadeBuffer, 0, read);
        var mixed = Math.Min(read, fadeRead);
        for (var i = 0; i < mixed; i++)
        {
            var progress = 1f - (_fadeSamplesRemaining / (float)_fadeSamplesTotal);
            progress = Math.Clamp(progress, 0f, 1f);
            buffer[offset + i] = buffer[offset + i] * (1f - progress) + fadeBuffer[i] * progress;
            _fadeSamplesRemaining--;
        }

        if (_fadeSamplesRemaining <= 0)
        {
            _fadeInReader.Dispose();
            _fadeInReader = null;
        }

        return read;
    }

    public void Clear()
    {
        _fadeInReader?.Dispose();
        _fadeInReader = null;
        _fadeSamplesRemaining = 0;
    }
}
