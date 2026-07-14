using LocalMusicHub.Models;

namespace LocalMusicHub.Services;

public static class FileOrganizerService
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars()
        .Concat(Path.GetInvalidPathChars())
        .Distinct()
        .ToArray();

    public static string BuildTargetPath(string template, LibraryTrack track, string organizeRoot)
    {
        var ext = Path.GetExtension(track.FilePath);
        if (string.IsNullOrWhiteSpace(ext))
            ext = "." + (string.IsNullOrWhiteSpace(track.Format) ? "mp3" : track.Format.ToLowerInvariant());

        var relative = ExpandTemplate(template, track, ext);
        relative = relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(organizeRoot, relative));
        return full;
    }

    public static string ResolveCollision(string path)
    {
        if (!File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(dir, $"{name} ({Guid.NewGuid():N}){ext}");
    }

    public static string SanitizeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Unknown";

        var trimmed = value.Trim();
        foreach (var ch in InvalidChars)
            trimmed = trimmed.Replace(ch, '_');

        return string.IsNullOrWhiteSpace(trimmed) ? "Unknown" : trimmed;
    }

    private static string ExpandTemplate(string template, LibraryTrack track, string ext)
    {
        var result = template;
        result = result.Replace("{artist}", SanitizeSegment(track.Artist), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{album_artist}", SanitizeSegment(
            string.IsNullOrWhiteSpace(track.AlbumArtist) ? track.Artist : track.AlbumArtist),
            StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{album}", SanitizeSegment(track.Album), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{title}", SanitizeSegment(track.DisplayTitle), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{genre}", SanitizeSegment(track.Genre), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{year}", track.Year is > 0 ? track.Year.Value.ToString() : "Unknown", StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{ext}", ext.TrimStart('.'), StringComparison.OrdinalIgnoreCase);

        var trackToken = track.TrackNumber is > 0 ? track.TrackNumber.Value.ToString("00") : "00";
        if (result.Contains("{track:00}", StringComparison.OrdinalIgnoreCase))
            result = result.Replace("{track:00}", trackToken, StringComparison.OrdinalIgnoreCase);
        else
            result = result.Replace("{track}", track.TrackNumber?.ToString() ?? "0", StringComparison.OrdinalIgnoreCase);

        if (!result.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            result += ext;

        return result;
    }
}
