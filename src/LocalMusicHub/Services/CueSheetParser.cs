using System.Globalization;
using System.Text.RegularExpressions;
using LocalMusicHub.Models;

namespace LocalMusicHub.Services;

public static class CuePathHelper
{
    public const string CueSuffix = "|cue:";

    public static string ResolveAudioPath(string storedPath)
    {
        var idx = storedPath.IndexOf(CueSuffix, StringComparison.Ordinal);
        return idx < 0 ? storedPath : storedPath[..idx];
    }

    public static string BuildVirtualPath(string audioPath, int cueTrackNumber) =>
        $"{audioPath}{CueSuffix}{cueTrackNumber:D2}";
}

public static class CueSheetParser
{
    private static readonly Regex IndexRegex = new(
        @"INDEX\s+01\s+(\d{1,2}):(\d{2}):(\d{2})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string? FindCueSheet(string audioPath)
    {
        var dir = Path.GetDirectoryName(audioPath);
        var stem = Path.GetFileNameWithoutExtension(audioPath);
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(stem))
            return null;

        var candidate = Path.Combine(dir, stem + ".cue");
        return File.Exists(candidate) ? candidate : null;
    }

    public static IReadOnlyList<LibraryTrack> ResolveTracksForFile(string audioPath, DateTime dateAddedUtc)
    {
        var baseTrack = AudioTagReader.Read(audioPath, dateAddedUtc);
        var cuePath = FindCueSheet(audioPath);
        if (cuePath is null)
            return [baseTrack];

        try
        {
            var cues = Parse(cuePath, audioPath, baseTrack, dateAddedUtc);
            return cues.Count > 0 ? cues : [baseTrack];
        }
        catch
        {
            return [baseTrack];
        }
    }

    public static List<LibraryTrack> Parse(string cuePath, string audioPath, LibraryTrack baseTrack, DateTime dateAddedUtc)
    {
        var lines = File.ReadAllLines(cuePath);
        var tracks = new List<(int Number, string Title, string Artist, TimeSpan Start)>();
        int? currentNumber = null;
        string? currentTitle = null;
        string? currentArtist = null;
        TimeSpan? currentStart = null;
        string album = baseTrack.Album;
        string albumArtist = baseTrack.AlbumArtist;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("REM ALBUM", StringComparison.OrdinalIgnoreCase))
                album = TrimQuotes(line["REM ALBUM".Length..]);
            else if (line.StartsWith("REM ALBUMARTIST", StringComparison.OrdinalIgnoreCase))
                albumArtist = TrimQuotes(line["REM ALBUMARTIST".Length..]);
            else if (line.StartsWith("TRACK", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out var num))
                    currentNumber = num;
            }
            else if (line.StartsWith("TITLE", StringComparison.OrdinalIgnoreCase))
                currentTitle = TrimQuotes(line["TITLE".Length..].Trim());
            else if (line.StartsWith("PERFORMER", StringComparison.OrdinalIgnoreCase))
                currentArtist = TrimQuotes(line["PERFORMER".Length..].Trim());
            else
            {
                var match = IndexRegex.Match(line);
                if (match.Success)
                {
                    var min = int.Parse(match.Groups[1].Value);
                    var sec = int.Parse(match.Groups[2].Value);
                    var frames = int.Parse(match.Groups[3].Value);
                    currentStart = TimeSpan.FromMinutes(min) + TimeSpan.FromSeconds(sec) +
                                   TimeSpan.FromMilliseconds(frames * 1000.0 / 75.0);
                }
            }
        }

        Flush();
        if (tracks.Count == 0)
            return [];

        var result = new List<LibraryTrack>();
        for (var i = 0; i < tracks.Count; i++)
        {
            var cue = tracks[i];
            var end = i < tracks.Count - 1
                ? tracks[i + 1].Start
                : baseTrack.Duration;
            var duration = end - cue.Start;
            if (duration <= TimeSpan.Zero)
                duration = TimeSpan.FromSeconds(1);

            result.Add(new LibraryTrack
            {
                FilePath = CuePathHelper.BuildVirtualPath(audioPath, cue.Number),
                Title = cue.Title,
                Artist = string.IsNullOrWhiteSpace(cue.Artist) ? baseTrack.Artist : cue.Artist,
                Album = album,
                AlbumArtist = albumArtist,
                TrackNumber = cue.Number,
                Year = baseTrack.Year,
                Genre = baseTrack.Genre,
                Duration = duration,
                Bitrate = baseTrack.Bitrate,
                Format = baseTrack.Format,
                DateAddedUtc = dateAddedUtc,
                FileModifiedUtc = baseTrack.FileModifiedUtc,
                CoverArt = baseTrack.CoverArt,
                CueStartMs = (int)Math.Round(cue.Start.TotalMilliseconds),
                CueEndMs = (int)Math.Round(end.TotalMilliseconds),
            });
        }

        return result;

        void Flush()
        {
            if (currentNumber is null || currentStart is null)
                return;
            tracks.Add((currentNumber.Value,
                string.IsNullOrWhiteSpace(currentTitle) ? $"Track {currentNumber}" : currentTitle!,
                currentArtist ?? "",
                currentStart.Value));
            currentNumber = null;
            currentTitle = null;
            currentArtist = null;
            currentStart = null;
        }
    }

    private static string TrimQuotes(string value)
    {
        value = value.Trim();
        if (value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2)
            return value[1..^1];
        return value;
    }
}
