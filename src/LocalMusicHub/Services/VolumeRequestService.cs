using System.Text.Json;

namespace LocalMusicHub.Services;

public sealed class VolumeSetRequest
{
    public double Volume { get; init; }
    public string RequestedUtc { get; init; } = "";
}

/// <summary>
/// Second-instance / Harbor IPC: set playback volume (0–1) without raising the window.
/// </summary>
public static class VolumeRequestService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string RequestPath =>
        Path.Combine(AppPaths.DataDirectory, "volume-request.json");

    public static bool TryReadPending(out VolumeSetRequest? request)
    {
        request = null;
        try
        {
            if (!File.Exists(RequestPath))
                return false;

            var json = File.ReadAllText(RequestPath);
            var payload = JsonSerializer.Deserialize<Payload>(json, JsonOptions);
            if (payload is null || payload.Volume is null)
                return false;

            request = new VolumeSetRequest
            {
                Volume = Clamp01(payload.Volume.Value),
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

    public static void WritePending(double volume)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var payload = new Payload
            {
                Volume = Clamp01(volume),
                RequestedUtc = DateTime.UtcNow.ToString("o"),
            };
            File.WriteAllText(RequestPath, JsonSerializer.Serialize(payload, JsonOptions));
        }
        catch
        {
            /* ignore */
        }
    }

    public static double Clamp01(double volume)
    {
        if (volume > 1.0 && volume <= 100.0)
            volume /= 100.0;
        return Math.Clamp(volume, 0, 1);
    }

    private sealed class Payload
    {
        public double? Volume { get; set; }
        public string? RequestedUtc { get; set; }
    }
}
