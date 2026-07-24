using LocalMusicHub.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LocalMusicHub.Services;

public static class PlaybackPipeline
{
    public static PlaybackPipelineResult Build(LibraryTrack track, AppSettings settings)
    {
        var reader = HubAudioReader.Open(track.AudioFilePath);
        ISampleProvider chain = reader;

        GaplessSampleProvider? gapless = null;
        var useTrackContainer = settings.GaplessEnabled || settings.CrossfadeEnabled;
        if (useTrackContainer)
        {
            gapless = new GaplessSampleProvider
            {
                AutoAdvance = settings.GaplessEnabled && !settings.CrossfadeEnabled,
            };
            gapless.SetCurrent(reader);
            chain = gapless;
        }

        CrossfadeSampleProvider? crossfade = null;
        if (settings.CrossfadeEnabled)
        {
            crossfade = new CrossfadeSampleProvider(chain, settings.CrossfadeSeconds, reader.WaveFormat.SampleRate);
            chain = crossfade;
        }

        var eq = new EqualizerSampleProvider(chain);
        eq.SetPreset(EqPresets.Get(settings));

        var gainDb = ResolveReplayGainDb(track, settings.ReplayGainMode);
        var linearGain = (float)Math.Pow(10, gainDb / 20.0);
        var volume = new SmoothVolumeSampleProvider(eq) { Volume = linearGain * (float)settings.DefaultVolume };
        var speed = new SpeedSampleProvider(volume)
        {
            Speed = (float)Math.Clamp(settings.PlaybackSpeed, 0.5, 2.0),
        };

        return new PlaybackPipelineResult(speed, reader, gapless, crossfade, volume, speed, linearGain);
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
    HubAudioReader Reader,
    GaplessSampleProvider? Gapless,
    CrossfadeSampleProvider? Crossfade,
    SmoothVolumeSampleProvider Volume,
    SpeedSampleProvider? Speed,
    float ReplayGainLinear);
