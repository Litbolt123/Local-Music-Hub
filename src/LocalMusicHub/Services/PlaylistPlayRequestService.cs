using System.Text.Json;

namespace LocalMusicHub.Services;

public sealed class PlaylistPlayRequest
{
    public string PlaylistName { get; init; } = "";
    public string RequestedUtc { get; init; } = "";
}

/// <summary>
/// Second-instance / Harbor IPC: play a playlist by name without raising the window.
/// </summary>
public static class PlaylistPlayRequestService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string RequestPath =>
        Path.Combine(AppPaths.DataDirectory, "playlist-request.json");

    public static bool TryReadPending(out PlaylistPlayRequest? request)
    {
        request = null;
        try
        {
            if (!File.Exists(RequestPath))
                return false;

            var json = File.ReadAllText(RequestPath);
            var payload = JsonSerializer.Deserialize<Payload>(json, JsonOptions);
            if (string.IsNullOrWhiteSpace(payload?.PlaylistName))
                return false;

            request = new PlaylistPlayRequest
            {
                PlaylistName = payload.PlaylistName.Trim(),
                RequestedUtc = payload.RequestedUtc ?? "",
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void ClearPending()
    {
        try
        {
            if (File.Exists(RequestPath))
                File.Delete(RequestPath);
        }
        catch
        {
            /* ignore */
        }
    }

    public static void WritePending(string playlistName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(playlistName))
                return;

            Directory.CreateDirectory(AppPaths.DataDirectory);
            var payload = new Payload
            {
                PlaylistName = playlistName.Trim(),
                RequestedUtc = DateTime.UtcNow.ToString("o"),
            };
            File.WriteAllText(RequestPath, JsonSerializer.Serialize(payload, JsonOptions));
        }
        catch
        {
            /* ignore */
        }
    }

    private sealed class Payload
    {
        public string PlaylistName { get; set; } = "";
        public string? RequestedUtc { get; set; }
    }
}
