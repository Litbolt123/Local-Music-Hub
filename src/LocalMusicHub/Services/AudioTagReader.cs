using LocalMusicHub.Models;
using TagFile = TagLib.File;

namespace LocalMusicHub.Services;

public static class AudioTagReader
{
    private static readonly string[] SupportedExtensions =
        [".mp3", ".m4a", ".aac", ".flac", ".wav", ".ogg", ".opus", ".wma", ".webm"];

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static LibraryTrack Read(string path, DateTime dateAddedUtc)
    {
        var info = new FileInfo(path);
        using var file = TagFile.Create(path);
        var tag = file.Tag;
        var props = file.Properties;

        var artist = FirstNonEmpty(tag.Performers);
        // Album artist is album-level metadata only — do not fall back to per-track performers
        // (featured artists on individual tracks would otherwise split one album into many).
        var albumArtist = FirstNonEmpty(tag.AlbumArtists);
        var album = tag.Album ?? "";
        var title = tag.Title ?? Path.GetFileNameWithoutExtension(path);
        var cover = tag.Pictures.FirstOrDefault()?.Data.Data;
        if (cover is not { Length: > 0 })
            cover = TryLoadFolderArt(path);

        var replayGain = ReadReplayGain(file);

        return new LibraryTrack
        {
            Id = 0,
            FilePath = path,
            Title = title,
            Artist = artist,
            Album = album,
            AlbumArtist = albumArtist,
            TrackNumber = tag.Track > 0 ? (int)tag.Track : null,
            Year = tag.Year > 0 ? (int)tag.Year : null,
            Genre = GenreNormalizer.NormalizeStored(
                tag.Genres.Length > 0 ? string.Join("; ", tag.Genres) : ""),
            Duration = props.Duration,
            Bitrate = props.AudioBitrate,
            Format = Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),
            DateAddedUtc = dateAddedUtc,
            FileModifiedUtc = info.LastWriteTimeUtc,
            CoverArt = cover is { Length: > 0 } ? cover : null,
            ReplayGainTrackDb = replayGain.TrackDb,
            ReplayGainAlbumDb = replayGain.AlbumDb,
            ReplayGainTrackPeak = replayGain.TrackPeak,
            ReplayGainAlbumPeak = replayGain.AlbumPeak,
        };
    }

    private static (double? TrackDb, double? AlbumDb, float? TrackPeak, float? AlbumPeak) ReadReplayGain(TagFile file)
    {
        try
        {
            var tagTypes = file.TagTypes;
            if (!tagTypes.HasFlag(TagLib.TagTypes.Id3v2))
                return (null, null, null, null);

            if (file.GetTag(TagLib.TagTypes.Id3v2) is not TagLib.Id3v2.Tag id3)
                return (null, null, null, null);

            double? trackDb = null;
            double? albumDb = null;
            float? trackPeak = null;
            float? albumPeak = null;

            foreach (var frame in id3.GetFrames<TagLib.Id3v2.UserTextInformationFrame>())
            {
                var desc = frame.Description?.ToUpperInvariant() ?? "";
                var text = frame.Text.FirstOrDefault() ?? "";
                if (desc.Contains("REPLAYGAIN_TRACK_GAIN", StringComparison.Ordinal))
                    trackDb = ParseGainDb(text);
                else if (desc.Contains("REPLAYGAIN_ALBUM_GAIN", StringComparison.Ordinal))
                    albumDb = ParseGainDb(text);
                else if (desc.Contains("REPLAYGAIN_TRACK_PEAK", StringComparison.Ordinal))
                    trackPeak = ParsePeak(text);
                else if (desc.Contains("REPLAYGAIN_ALBUM_PEAK", StringComparison.Ordinal))
                    albumPeak = ParsePeak(text);
            }

            return (trackDb, albumDb, trackPeak, albumPeak);
        }
        catch
        {
            return (null, null, null, null);
        }
    }

    private static byte[]? TryLoadFolderArt(string audioPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(audioPath);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return null;

            string[] names =
            [
                "cover.jpg", "cover.jpeg", "cover.png", "cover.webp",
                "folder.jpg", "folder.jpeg", "folder.png",
                "AlbumArt.jpg", "AlbumArtSmall.jpg", "Front.jpg", "front.jpg",
            ];

            foreach (var name in names)
            {
                var candidate = Path.Combine(dir, name);
                if (!File.Exists(candidate))
                    continue;
                var bytes = File.ReadAllBytes(candidate);
                if (bytes.Length > 0)
                    return bytes;
            }

            // Any image in the folder named like album art (first match).
            foreach (var file in Directory.EnumerateFiles(dir)
                         .Where(f => Path.GetExtension(f) is ".jpg" or ".jpeg" or ".png" or ".webp")
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                         .Take(8))
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                if (stem.Contains("cover", StringComparison.OrdinalIgnoreCase) ||
                    stem.Contains("folder", StringComparison.OrdinalIgnoreCase) ||
                    stem.Contains("front", StringComparison.OrdinalIgnoreCase) ||
                    stem.Contains("album", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = File.ReadAllBytes(file);
                    if (bytes.Length > 0)
                        return bytes;
                }
            }
        }
        catch
        {
            /* ignore unreadable folder art */
        }

        return null;
    }

    private static double? ParseGainDb(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim().TrimEnd('d', 'B');
        return double.TryParse(value, out var db) ? db : null;
    }

    private static float? ParsePeak(string value) =>
        float.TryParse(value.Trim(), out var peak) ? peak : null;

    private static string FirstNonEmpty(params string[][] valueGroups)
    {
        foreach (var values in valueGroups)
        {
            var joined = string.Join(", ", values.Where(v => !string.IsNullOrWhiteSpace(v)));
            if (!string.IsNullOrWhiteSpace(joined))
                return joined;
        }

        return "";
    }
}
