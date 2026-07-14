using System.Text.Json;

namespace LocalMusicHub.Services;

public static class SettingsTransferService
{
    public static bool Export(AppSettings settings, string destinationPath)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, AppSettingsService.SerializerOptions);
            File.WriteAllText(destinationPath, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static (AppSettings? settings, string? error) Import(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return (null, "File not found.");

            var json = File.ReadAllText(sourcePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, AppSettingsService.SerializerOptions);
            return settings is null
                ? (null, "Could not read settings from file.")
                : (settings, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}
