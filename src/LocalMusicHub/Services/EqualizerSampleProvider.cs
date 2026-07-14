using NAudio.Dsp;
using NAudio.Wave;

namespace LocalMusicHub.Services;

public sealed class EqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly BiQuadFilter[] _filters;

    public EqualizerSampleProvider(ISampleProvider source, int bandCount = 10)
    {
        _source = source;
        WaveFormat = source.WaveFormat;
        _filters = new BiQuadFilter[bandCount];
        for (var i = 0; i < bandCount; i++)
            _filters[i] = BiQuadFilter.PeakingEQ(44100, 1000, 1.0f, 0);
        SetPreset(EqPresets.Flat);
    }

    public WaveFormat WaveFormat { get; }

    public void SetPreset(IReadOnlyList<float> bandGainsDb)
    {
        var sampleRate = WaveFormat.SampleRate;
        var frequencies = EqPresets.Frequencies;
        for (var i = 0; i < _filters.Length; i++)
        {
            var freq = frequencies[Math.Min(i, frequencies.Length - 1)];
            var gain = i < bandGainsDb.Count ? bandGainsDb[i] : 0f;
            _filters[i] = BiQuadFilter.PeakingEQ(sampleRate, freq, 1.0f, gain);
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        for (var n = 0; n < read; n++)
        {
            var sample = buffer[offset + n];
            foreach (var filter in _filters)
                sample = filter.Transform(sample);
            buffer[offset + n] = sample;
        }

        return read;
    }
}

public static class EqPresets
{
    public static readonly float[] Frequencies =
        [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    public static IReadOnlyList<float> Flat => [0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    public static IReadOnlyList<float> BassBoost => [6, 5, 4, 2, 0, 0, 0, 0, 0, 0];
    public static IReadOnlyList<float> Vocal => [-2, -1, 0, 2, 4, 4, 3, 1, 0, -1];
    public static IReadOnlyList<float> Treble => [0, 0, 0, 0, 0, 1, 2, 4, 5, 6];

    public static IReadOnlyList<float> Get(string preset) => preset.ToLowerInvariant() switch
    {
        "bass" or "bassboost" => BassBoost,
        "vocal" => Vocal,
        "treble" => Treble,
        "custom" => Flat, // callers should use Get(settings) for custom bands
        _ => Flat,
    };

    public static IReadOnlyList<float> Get(AppSettings settings)
    {
        if (string.Equals(settings.EqPreset, "custom", StringComparison.OrdinalIgnoreCase) &&
            settings.CustomEqBands is { Count: > 0 })
        {
            var bands = new float[Frequencies.Length];
            for (var i = 0; i < bands.Length; i++)
                bands[i] = i < settings.CustomEqBands.Count
                    ? Math.Clamp(settings.CustomEqBands[i], -12f, 12f)
                    : 0f;
            return bands;
        }

        return Get(settings.EqPreset);
    }
}
