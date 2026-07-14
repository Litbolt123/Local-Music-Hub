using LocalMusicHub.Models;

namespace LocalMusicHub.Services;

public static class DuplicateScoring
{
    public static int Score(LibraryTrack track)
    {
        var score = track.Bitrate * 10;
        score += FormatRank(track.Format) * 100;
        score += track.Rating * 40;
        score += Math.Min(track.PlayCount, 25) * 8;
        if (track.CoverArt is { Length: > 0 })
            score += 30;
        if (LyricsService.HasLocalLyrics(track.FilePath))
            score += 20;
        if (track.ReplayGainTrackDb is not null || track.ReplayGainAlbumDb is not null)
            score += 10;
        // Prefer shorter paths slightly (often the "organized" copy).
        score += Math.Max(0, 40 - Math.Min(40, track.FilePath.Length / 8));
        return score;
    }

    public static LibraryTrack PickBest(IReadOnlyList<LibraryTrack> tracks) =>
        tracks.OrderByDescending(Score).ThenBy(t => t.FilePath, StringComparer.OrdinalIgnoreCase).First();

    public static string QualityLabel(LibraryTrack track)
    {
        var bits = track.Bitrate > 0 ? $"{track.Bitrate} kbps" : "?—kbps";
        var fmt = string.IsNullOrWhiteSpace(track.Format) ? "?" : track.Format;
        return $"{fmt} · {bits} · {track.RatingLabel}";
    }

    private static int FormatRank(string format) =>
        format.Trim().ToUpperInvariant() switch
        {
            "FLAC" or "ALAC" or "WAV" or "AIFF" or "APE" or "WV" => 5,
            "OPUS" => 4,
            "AAC" or "M4A" => 3,
            "MP3" => 2,
            "OGG" or "OGA" => 2,
            "WMA" => 1,
            _ => 0,
        };
}
