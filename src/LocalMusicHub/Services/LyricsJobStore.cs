using System.Text.Json;

namespace LocalMusicHub.Services;

public sealed class LyricsJobSnapshot
{
    public List<string> PendingPaths { get; set; } = [];
    public int JobTotal { get; set; }
    public int JobDone { get; set; }
    public int JobSaved { get; set; }
    public int JobAlreadyHad { get; set; }
    public int JobNotFound { get; set; }
    public int JobSkippedNotFound { get; set; }
}

/// <summary>Persists an in-progress bulk lyrics job so it can resume after restart.</summary>
public static class LyricsJobStore
{
    public static string StorePath => Path.Combine(AppPaths.DataDirectory, "lyrics-job.json");

    public static LyricsJobSnapshot? Load()
    {
        try
        {
            if (!File.Exists(StorePath))
                return null;
            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<LyricsJobSnapshot>(json, AppSettingsService.SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(LyricsJobSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var json = JsonSerializer.Serialize(snapshot, AppSettingsService.SerializerOptions);
            File.WriteAllText(StorePath, json);
        }
        catch
        {
            /* ignore */
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(StorePath))
                File.Delete(StorePath);
        }
        catch
        {
            /* ignore */
        }
    }
}
