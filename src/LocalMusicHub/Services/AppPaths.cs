using System.Text.Json;

namespace LocalMusicHub.Services;

public static class AppPaths
{
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalMusicHub");

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
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
        try
        {
            if (!File.Exists(AppPaths.SettingsPath))
                return CreateDefault();

            var json = File.ReadAllText(AppPaths.SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? CreateDefault();
        }
        catch
        {
            return CreateDefault();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(AppPaths.SettingsPath, json);
    }

    private static AppSettings CreateDefault() => new()
    {
        LibraryFolders = [AppPaths.DefaultMusicFolder],
        UseDarkTheme = true,
    };
}
