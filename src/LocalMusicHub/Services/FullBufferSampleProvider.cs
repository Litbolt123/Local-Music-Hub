using NAudio.Wave;

namespace LocalMusicHub.Services;

/// <summary>
/// Ensures each Read fills the requested buffer (or hits true EOF).
/// Some decoders return short mid-stream reads; that underruns Wasapi and sounds like a click/zzzt.
/// </summary>
public sealed class FullBufferSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;

    public FullBufferSampleProvider(ISampleProvider source) => _source = source;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = _source.Read(buffer, offset + total, count - total);
            if (read <= 0)
                break;
            total += read;
        }

        return total;
    }
}
