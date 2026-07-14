using System.Text;
using LocalMusicHub.Data;
using LocalMusicHub.Models;

namespace LocalMusicHub.Services;

public static class M3uPlaylistService
{
    public static void Export(string path, string playlistName, IReadOnlyList<LibraryTrack> tracks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine($"#PLAYLIST:{playlistName}");
        foreach (var track in tracks)
        {
            var seconds = Math.Max(0, (int)Math.Round(track.Duration.TotalSeconds));
            sb.AppendLine($"#EXTINF:{seconds},{EscapeInf(track.DisplayArtist)} - {EscapeInf(track.DisplayTitle)}");
            sb.AppendLine(track.FilePath);
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static (string Name, List<string> Paths) Parse(string path)
    {
        var lines = File.ReadAllLines(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var paths = new List<string>();
        var baseDir = Path.GetDirectoryName(path) ?? "";

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') && !line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                if (line.StartsWith("#PLAYLIST:", StringComparison.OrdinalIgnoreCase))
                    name = line["#PLAYLIST:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                continue;

            var resolved = line;
            if (!Path.IsPathRooted(resolved))
                resolved = Path.GetFullPath(Path.Combine(baseDir, resolved));
            paths.Add(resolved);
        }

        return (name, paths);
    }

    public static LibraryPlaylist Import(LibraryRepository repository, string filePath)
    {
        var (name, paths) = Parse(filePath);
        var trackIds = new List<long>();
        foreach (var path in paths)
        {
            var track = repository.GetTrackByPath(path);
            if (track is { Id: > 0 })
                trackIds.Add(track.Id);
        }

        if (trackIds.Count == 0)
            throw new InvalidOperationException("No matching library tracks found for that M3U file.");

        var playlist = repository.CreatePlaylist(string.IsNullOrWhiteSpace(name) ? "Imported playlist" : name);
        repository.AddTracksToPlaylist(playlist.Id, trackIds);
        return playlist;
    }

    private static string EscapeInf(string value) =>
        value.Replace(',', ' ').Trim();
}
