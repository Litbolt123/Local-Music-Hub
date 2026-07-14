using LocalMusicHub.Models;
using TagFile = TagLib.File;

namespace LocalMusicHub.Services;

public static class AudioTagWriter
{
    public static void Write(LibraryTrack track)
    {
        if (track.FilePath.Contains(CuePathHelper.CueSuffix, StringComparison.Ordinal))
            throw new InvalidOperationException("Cannot write audio tags for a CUE virtual track.");

        using var file = TagFile.Create(track.AudioFilePath);
        var tag = file.Tag;

        tag.Title = track.Title;
        tag.Performers = string.IsNullOrWhiteSpace(track.Artist)
            ? []
            : track.Artist.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        tag.AlbumArtists = string.IsNullOrWhiteSpace(track.AlbumArtist)
            ? []
            : [track.AlbumArtist.Trim()];
        tag.Album = track.Album;
        tag.Track = track.TrackNumber is > 0 ? (uint)track.TrackNumber.Value : 0;
        tag.Year = track.Year is > 0 ? (uint)track.Year.Value : 0;
        tag.Genres = string.IsNullOrWhiteSpace(track.Genre)
            ? []
            : GenreNormalizer.SplitGenres(track.Genre).ToArray();

        if (track.CoverArt is { Length: > 0 })
        {
            tag.Pictures =
            [
                new TagLib.Picture(new TagLib.ByteVector(track.CoverArt))
                {
                    Type = TagLib.PictureType.FrontCover,
                    MimeType = GuessMime(track.CoverArt),
                    Description = "Cover",
                },
            ];
        }

        file.Save();
    }

    public static void WriteCoverOnly(string filePath, byte[] coverBytes)
    {
        CoverArtHelper.WriteCoverToFile(filePath, coverBytes);
    }

    /// <summary>Writes plain lyrics into the file tag (USLT / Vorbis LYRICS). Synced LRC stays in sidecars.</summary>
    public static bool TryWriteLyrics(string filePath, string lyrics)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || string.IsNullOrWhiteSpace(lyrics))
            return false;

        try
        {
            using var file = TagFile.Create(filePath);
            // Prefer plain text in tags; strip LRC timestamps if present.
            var plain = lyrics.Contains('[') && lyrics.Contains(']')
                ? StripLrc(lyrics)
                : lyrics.Trim();
            if (string.IsNullOrWhiteSpace(plain))
                return false;

            file.Tag.Lyrics = plain;
            file.Save();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string? TryReadLyrics(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        try
        {
            using var file = TagFile.Create(filePath);
            var lyrics = file.Tag.Lyrics?.Trim();
            return string.IsNullOrWhiteSpace(lyrics) ? null : lyrics;
        }
        catch
        {
            return null;
        }
    }

    private static string StripLrc(string lrc)
    {
        var lines = lrc.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(Environment.NewLine,
            lines.Select(line => System.Text.RegularExpressions.Regex.Replace(
                    line, @"\[\d{1,2}:\d{2}([.:]\d{1,3})?\]", "").Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('[')));
    }

    private static string GuessMime(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return "image/jpeg";
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return "image/png";
        return "image/jpeg";
    }
}
