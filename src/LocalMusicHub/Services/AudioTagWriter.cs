using LocalMusicHub.Models;
using TagFile = TagLib.File;

namespace LocalMusicHub.Services;

public static class AudioTagWriter
{
    public static void Write(LibraryTrack track)
    {
        if (track.FilePath.Contains(CuePathHelper.CueSuffix, StringComparison.Ordinal))
            throw new InvalidOperationException("Cannot write audio tags for a CUE virtual track.");

        var coverBytes = track.CoverArt is { Length: > 0 }
            ? CoverArtHelper.NormalizeDownloadedCover(track.CoverArt, outputSize: 1200, quality: 90) ?? track.CoverArt
            : null;

        WriteWithRetry(track.AudioFilePath, file =>
        {
            var ext = Path.GetExtension(track.AudioFilePath);
            var container = ClassifyContainer(ext);

            // Windows Explorer Details reads Vorbis comments on FLAC/OGG — not ID3 glued on the front.
            // ID3v2.3 is only forced for MP3 (Explorer preference).
            byte previousId3Version = TagLib.Id3v2.Tag.DefaultVersion;
            var previousForceId3 = TagLib.Id3v2.Tag.ForceDefaultVersion;
            try
            {
                if (container == TagContainer.Mpeg)
                {
                    TagLib.Id3v2.Tag.DefaultVersion = 3;
                    TagLib.Id3v2.Tag.ForceDefaultVersion = true;
                }
                else
                {
                    TagLib.Id3v2.Tag.ForceDefaultVersion = false;
                }

                if (container == TagContainer.Xiph)
                    StripId3ForWindowsShell(file);

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
                tag.Comment = track.Comment?.Trim() ?? "";
                WriteDateReleased(file, track.DateReleased, container);

                if (coverBytes is { Length: > 0 })
                {
                    tag.Pictures =
                    [
                        new TagLib.Picture(new TagLib.ByteVector(coverBytes))
                        {
                            Type = TagLib.PictureType.FrontCover,
                            MimeType = GuessMime(coverBytes),
                            Description = "Cover",
                        },
                    ];
                }

                file.Save();
            }
            finally
            {
                TagLib.Id3v2.Tag.DefaultVersion = previousId3Version;
                TagLib.Id3v2.Tag.ForceDefaultVersion = previousForceId3;
            }
        });
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
            if (ClassifyContainer(Path.GetExtension(filePath)) == TagContainer.Xiph)
                StripId3ForWindowsShell(file);

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

    /// <summary>
    /// Removes ID3 from FLAC/Ogg so the file starts with fLaC/OggS again — required for Windows Properties Details.
    /// </summary>
    public static bool TryRepairWindowsVisibleTags(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;
        if (ClassifyContainer(Path.GetExtension(filePath)) != TagContainer.Xiph)
            return false;

        try
        {
            var repaired = false;
            WriteWithRetry(filePath, file =>
            {
                if ((file.TagTypes & (TagLib.TagTypes.Id3v1 | TagLib.TagTypes.Id3v2)) == TagLib.TagTypes.None)
                    return;

                StripId3ForWindowsShell(file);
                _ = file.Tag.Title;
                file.Save();
                repaired = true;
            });
            return repaired;
        }
        catch
        {
            return false;
        }
    }

    private static void StripId3ForWindowsShell(TagFile file)
    {
        file.RemoveTags(TagLib.TagTypes.Id3v1 | TagLib.TagTypes.Id3v2);
        // Ensure native Vorbis comment block exists for subsequent writes.
        _ = file.GetTag(TagLib.TagTypes.Xiph, create: true);
    }

    private static void WriteWithRetry(string path, Action<TagFile> write, int attempts = 5)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                if (!AudioFileAccess.WaitUntilReadableAsync(path, TimeSpan.FromSeconds(12))
                        .GetAwaiter().GetResult())
                {
                    throw new IOException($"Audio file is not ready: {path}");
                }

                using var file = TagFile.Create(path);
                write(file);
                return;
            }
            catch (Exception ex) when (AudioFileAccess.IsSharingViolation(ex) && attempt < attempts - 1)
            {
                last = ex;
                Thread.Sleep(250 * (attempt + 1));
            }
        }

        throw last ?? new IOException($"Could not write tags: {path}");
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

    private static void WriteDateReleased(TagFile file, string? dateReleased, TagContainer container)
    {
        var value = dateReleased?.Trim() ?? "";

        if (container == TagContainer.Xiph ||
            file.GetTag(TagLib.TagTypes.Xiph) is TagLib.Ogg.XiphComment)
        {
            if (file.GetTag(TagLib.TagTypes.Xiph, create: container == TagContainer.Xiph)
                is TagLib.Ogg.XiphComment xiph)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    xiph.RemoveField("RELEASEDATE");
                }
                else
                {
                    xiph.SetField("RELEASEDATE", value);
                    if (value.Length >= 4 && int.TryParse(value.AsSpan(0, 4), out var year))
                        xiph.SetField("DATE", year.ToString());
                }
            }
        }

        // Never create ID3 on FLAC/OGG — that prefixes the file and blanks Windows Details.
        if (container == TagContainer.Mpeg &&
            file.GetTag(TagLib.TagTypes.Id3v2, create: true) is TagLib.Id3v2.Tag id3)
        {
            id3.RemoveFrames("TDRL");
            if (!string.IsNullOrWhiteSpace(value))
                id3.SetTextFrame("TDRL", value);
        }
    }

    private enum TagContainer { Other, Mpeg, Xiph }

    private static TagContainer ClassifyContainer(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return TagContainer.Other;

        if (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mp2", StringComparison.OrdinalIgnoreCase))
            return TagContainer.Mpeg;

        if (extension.Equals(".flac", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".oga", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".opus", StringComparison.OrdinalIgnoreCase))
            return TagContainer.Xiph;

        return TagContainer.Other;
    }
}
