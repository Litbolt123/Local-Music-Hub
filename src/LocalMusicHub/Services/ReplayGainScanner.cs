using LocalMusicHub.Data;
using LocalMusicHub.Models;
using NAudio.Wave;

namespace LocalMusicHub.Services;

public sealed class ReplayGainScanner
{
    private readonly LibraryRepository _repository;

    public ReplayGainScanner(LibraryRepository repository) => _repository = repository;

    public async Task<int> ScanAsync(
        IEnumerable<LibraryTrack> tracks,
        IProgress<(int Done, int Total, string Path)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var list = tracks.ToList();
        var done = 0;
        await Task.Run(() =>
        {
            foreach (var track in list)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report((++done, list.Count, track.FilePath));
                try
                {
                    var analysis = Analyze(track.FilePath);
                    _repository.UpdateReplayGain(track.Id, analysis.TrackGainDb, analysis.TrackPeak);
                }
                catch
                {
                    /* skip unreadable */
                }
            }
        }, cancellationToken).ConfigureAwait(true);
        return done;
    }

    public static ReplayGainAnalysis Analyze(string path)
    {
        using var reader = new AudioFileReader(path);
        var sampleProvider = reader.ToSampleProvider();
        var buffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
        double sumSquares = 0;
        long samples = 0;
        float peak = 0;

        int read;
        while ((read = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var sample = Math.Abs(buffer[i]);
                if (sample > peak)
                    peak = sample;
                sumSquares += buffer[i] * buffer[i];
                samples++;
            }
        }

        if (samples == 0)
            return new ReplayGainAnalysis(0, 1);

        var rms = Math.Sqrt(sumSquares / samples);
        var rmsDb = 20 * Math.Log10(Math.Max(rms, 1e-9));
        var targetDb = -18.0;
        var gainDb = targetDb - rmsDb;
        gainDb = Math.Clamp(gainDb, -24, 24);
        return new ReplayGainAnalysis(gainDb, Math.Max(peak, 1e-9f));
    }
}

public readonly record struct ReplayGainAnalysis(double TrackGainDb, float TrackPeak);
