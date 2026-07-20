using System.Text.Json;

namespace LocalMusicHub.Services;

public static class AppPaths
{
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalMusicHub");

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static string SettingsBackupPath => Path.Combine(DataDirectory, "settings.json.bak");
    public static string DatabasePath => Path.Combine(DataDirectory, "library.db");

    public static string YouTubeDownloaderDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YouTubeToMp3");

    public static string YouTubeDownloaderSettingsPath =>
        Path.Combine(YouTubeDownloaderDataDirectory, "settings.json");

    public static string YouTubeDownloaderHistoryPath =>
        Path.Combine(YouTubeDownloaderDataDirectory, "history.json");

    public static string DefaultMusicFolder =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
}

public static class AppSettingsService
{
    public static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        var settings = TryLoad(AppPaths.SettingsPath);
        if (settings is not null)
            return settings;

        settings = TryLoad(AppPaths.SettingsBackupPath);
        if (settings is not null)
        {
            try { Save(settings); } catch { /* best-effort restore */ }
            return settings;
        }

        return CreateDefault();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        var path = AppPaths.SettingsPath;
        var tempPath = path + ".tmp";

        File.WriteAllText(tempPath, json);
        if (File.Exists(path))
            File.Copy(path, AppPaths.SettingsBackupPath, overwrite: true);

        if (File.Exists(path))
            File.Replace(tempPath, path, AppPaths.SettingsBackupPath);
        else
            File.Move(tempPath, path);
    }

    private static AppSettings? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static AppSettings CreateDefault() => new()
    {
        LibraryFolders = [AppPaths.DefaultMusicFolder],
        UseDarkTheme = true,
    };

    public static void EnsureLibraryIngestToken(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.LibraryIngestToken))
            settings.LibraryIngestToken = Guid.NewGuid().ToString("N");
    }
}
