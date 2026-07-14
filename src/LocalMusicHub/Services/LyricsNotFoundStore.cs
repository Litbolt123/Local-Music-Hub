using System.Text.Json;

namespace LocalMusicHub.Services;

/// <summary>Tracks LRCLIB misses so bulk sweeps do not retry instrumentals / unavailable lyrics.</summary>
public static class LyricsNotFoundStore
{
    private static readonly object Gate = new();
    private static Dictionary<string, DateTime> _cache = Load();

    public static string StorePath => Path.Combine(AppPaths.DataDirectory, "lyrics-not-found.json");

    public static bool IsMarked(string? audioPath)
    {
        if (string.IsNullOrWhiteSpace(audioPath))
            return false;
        var key = NormalizeKey(audioPath);
        lock (Gate)
            return _cache.ContainsKey(key);
    }

    public static void Mark(string? audioPath)
    {
        if (string.IsNullOrWhiteSpace(audioPath))
            return;
        var key = NormalizeKey(audioPath);
        lock (Gate)
        {
            _cache[key] = DateTime.UtcNow;
            SaveLocked();
        }
    }

    public static void Clear(string? audioPath)
    {
        if (string.IsNullOrWhiteSpace(audioPath))
            return;
        var key = NormalizeKey(audioPath);
        lock (Gate)
        {
            if (!_cache.Remove(key))
                return;
            SaveLocked();
        }
    }

    public static IReadOnlyList<string> GetAllPaths()
    {
        lock (Gate)
            return _cache.Keys.ToList();
    }

    private static string NormalizeKey(string path) =>
        path.Trim().ToLowerInvariant();

    private static Dictionary<string, DateTime> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
                return new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            var json = File.ReadAllText(StorePath);
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json, AppSettingsService.SerializerOptions);
            if (raw is null)
                return new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in raw)
            {
                if (DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    result[key] = dt;
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var payload = _cache.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ToString("O"),
                StringComparer.OrdinalIgnoreCase);
            var json = JsonSerializer.Serialize(payload, AppSettingsService.SerializerOptions);
            File.WriteAllText(StorePath, json);
        }
        catch
        {
            /* ignore */
        }
    }
}
