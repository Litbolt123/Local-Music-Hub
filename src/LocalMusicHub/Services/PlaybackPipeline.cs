using LocalMusicHub.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LocalMusicHub.Services;

public static class PlaybackPipeline
{
    public static PlaybackPipelineResult Build(LibraryTrack track, AppSettings settings)
    {
        var reader = new AudioFileReader(track.AudioFilePath);
        ISampleProvider chain = reader.ToSampleProvider();

        GaplessSampleProvider? gapless = null;
        if (settings.GaplessEnabled)
        {
            gapless = new GaplessSampleProvider();
            gapless.SetCurrent(reader);
            chain = gapless;
        }

        var eq = new EqualizerSampleProvider(chain);
        eq.SetPreset(EqPresets.Get(settings));

        var gainDb = ResolveReplayGainDb(track, settings.ReplayGainMode);
        var linearGain = (float)Math.Pow(10, gainDb / 20.0);
        var volume = new VolumeSampleProvider(eq) { Volume = linearGain * (float)settings.DefaultVolume };
        var speed = new SpeedSampleProvider(volume)
        {
            Speed = (float)Math.Clamp(settings.PlaybackSpeed, 0.5, 2.0),
        };

        CrossfadeSampleProvider? crossfade = null;
        ISampleProvider provider = speed;
        if (settings.CrossfadeEnabled)
        {
            crossfade = new CrossfadeSampleProvider(speed, settings.CrossfadeSeconds, reader.WaveFormat.SampleRate);
            provider = crossfade;
        }

        return new PlaybackPipelineResult(provider, reader, gapless, crossfade, volume, linearGain);
    }

    public static double ResolveReplayGainDb(LibraryTrack track, string mode)
    {
        if (string.Equals(mode, "off", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (string.Equals(mode, "album", StringComparison.OrdinalIgnoreCase))
            return track.ReplayGainAlbumDb ?? track.ReplayGainTrackDb ?? 0;

        return track.ReplayGainTrackDb ?? track.ReplayGainAlbumDb ?? 0;
    }
}

public readonly record struct PlaybackPipelineResult(
    ISampleProvider Provider,
    AudioFileReader Reader,
    GaplessSampleProvider? Gapless,
    CrossfadeSampleProvider? Crossfade,
    VolumeSampleProvider Volume,
    float ReplayGainLinear);
