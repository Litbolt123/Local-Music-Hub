using System.IO;
using System.Text.Json;
using LocalMusicHub.Models;
using LocalMusicHub.Services;

namespace LocalMusicHub.Services;

/// <summary>Lightweight playback state for sibling apps (App Folder music panel).</summary>
public static class NowPlayingStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string StatePath => Path.Combine(AppPaths.DataDirectory, "now-playing.json");

    public static void Write(LibraryTrack? track, bool isPlaying, bool isPaused)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            if (track is null)
            {
                if (File.Exists(StatePath))
                    File.Delete(StatePath);
                return;
            }

            var payload = new NowPlayingState
            {
                Title = track.DisplayTitle,
                Artist = track.DisplayArtist,
                Album = track.DisplayAlbum,
                IsPlaying = isPlaying,
                IsPaused = isPaused,
                UpdatedUtc = DateTime.UtcNow.ToString("o"),
            };
            File.WriteAllText(StatePath, JsonSerializer.Serialize(payload, JsonOptions));
        }
        catch
        {
            /* ignore */
        }
    }

    private sealed class NowPlayingState
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public string? UpdatedUtc { get; set; }
    }
}
