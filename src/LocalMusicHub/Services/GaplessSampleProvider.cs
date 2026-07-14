using NAudio.Wave;

namespace LocalMusicHub.Services;

public sealed class GaplessSampleProvider : ISampleProvider
{
    private AudioFileReader? _current;
    private AudioFileReader? _next;
    private readonly object _gate = new();

    public GaplessSampleProvider()
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    }

    public WaveFormat WaveFormat { get; private set; }

    public AudioFileReader? CurrentReader
    {
        get { lock (_gate) return _current; }
    }

    public void SetCurrent(AudioFileReader reader)
    {
        lock (_gate)
        {
            _current?.Dispose();
            _next?.Dispose();
            _current = reader;
            _next = null;
            WaveFormat = reader.WaveFormat;
        }
    }

    public void PreloadNext(AudioFileReader reader)
    {
        lock (_gate)
        {
            _next?.Dispose();
            _next = reader;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _current?.Dispose();
            _next?.Dispose();
            _current = null;
            _next = null;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_gate)
        {
            if (_current is null)
                return 0;

            var read = _current.ToSampleProvider().Read(buffer, offset, count);
            if (read < count && _next is not null)
            {
                var nextProvider = _next.ToSampleProvider();
                if (nextProvider.WaveFormat.SampleRate != WaveFormat.SampleRate ||
                    nextProvider.WaveFormat.Channels != WaveFormat.Channels)
                {
                    return read;
                }

                _current.Dispose();
                _current = _next;
                _next = null;
                WaveFormat = _current.WaveFormat;
                read += _current.ToSampleProvider().Read(buffer, offset + read, count - read);
            }

            return read;
        }
    }
}
