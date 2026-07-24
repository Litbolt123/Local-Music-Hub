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
            var playlistOnly = ForwardPlaylistArgs(args);
            var volumeOnly = ForwardVolumeArgs(args);

            // Harbor greeting: --minimized with playlist/volume should stay in tray.
            var stayInTray = AutoStartService.ArgsRequestTray(args) &&
                             string.IsNullOrWhiteSpace(ParseImportPath(args)) &&
                             (playlistOnly || volumeOnly);
            if (!stayInTray)
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

    /// <returns>True when a playlist name was forwarded.</returns>
    private static bool ForwardPlaylistArgs(string[] args)
    {
        var name = ParsePlaylistName(args);
        if (string.IsNullOrWhiteSpace(name))
            return false;

        PlaylistPlayRequestService.WritePending(name);
        return true;
    }

    /// <returns>True when a volume was forwarded.</returns>
    private static bool ForwardVolumeArgs(string[] args)
    {
        var volume = ParseVolume(args);
        if (volume is null)
            return false;

        VolumeRequestService.WritePending(volume.Value);
        return true;
    }

    private static string? ParsePlaylistName(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--playlist", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1].Trim().Trim('"');

            if (args[i].StartsWith("--playlist=", StringComparison.OrdinalIgnoreCase))
                return args[i]["--playlist=".Length..].Trim().Trim('"');
        }

        return null;
    }

    private static double? ParseVolume(string[] args)
    {
        string? raw = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--volume", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                raw = args[i + 1].Trim().Trim('"');
                break;
            }

            if (args[i].StartsWith("--volume=", StringComparison.OrdinalIgnoreCase))
            {
                raw = args[i]["--volume=".Length..].Trim().Trim('"');
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(raw) ||
            !double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return null;

        return VolumeRequestService.Clamp01(v);
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
