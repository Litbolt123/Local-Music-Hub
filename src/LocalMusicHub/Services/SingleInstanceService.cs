using System.Text.Json;

namespace LocalMusicHub.Services;

public static class SingleInstanceService
{
    public const string MutexName = @"Global\LocalMusicHub_SingleInstance";

    public static string ActivateSignalPath =>
        Path.Combine(AppPaths.DataDirectory, "activate.signal");

    public static bool TryBecomePrimaryInstance(out Mutex? mutex)
    {
        mutex = new Mutex(true, MutexName, out var createdNew);
        if (createdNew)
            return true;

        mutex.Dispose();
        mutex = null;
        return false;
    }

    public static void NotifyPrimaryInstance(string[] args)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            ForwardImportArgs(args);
            File.WriteAllText(ActivateSignalPath, DateTime.UtcNow.ToString("o"));
        }
        catch
        {
            /* ignore */
        }
    }

    private static void ForwardImportArgs(string[] args)
    {
        var path = ParseImportPath(args);
        if (string.IsNullOrWhiteSpace(path))
            return;

        var importFolder = args.Any(a =>
            string.Equals(a, "--import-folder", StringComparison.OrdinalIgnoreCase));

        LibraryImportRequestService.WritePending(path, importFolder);
    }

    private static string? ParseImportPath(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--import", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1].Trim('"');

            if (args[i].StartsWith("--import=", StringComparison.OrdinalIgnoreCase))
                return args[i]["--import=".Length..].Trim('"');
        }

        return null;
    }
}
