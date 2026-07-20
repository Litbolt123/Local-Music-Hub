using NAudio.Wave;

namespace LocalMusicHub.Services;

internal static class AudioFileReaders
{
    /// <summary>
    /// AudioFileReader already implements ISampleProvider; avoid ToSampleProvider() wrappers
    /// so CurrentTime stays aligned with the stream driving playback.
    /// </summary>
    public static ISampleProvider AsSamples(AudioFileReader reader) => reader;
}
