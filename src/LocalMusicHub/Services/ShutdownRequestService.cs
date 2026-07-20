namespace LocalMusicHub.Services;

/// <summary>
/// Lets build scripts request a graceful app exit (flush settings) before force-kill.
/// </summary>
public static class ShutdownRequestService
{
    public static string RequestPath => Path.Combine(AppPaths.DataDirectory, "shutdown.request");

    public static void SignalShutdown()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        File.WriteAllText(RequestPath, DateTime.UtcNow.ToString("o"));
    }

    public static bool TryConsumeShutdownRequest()
    {
        try
        {
            if (!File.Exists(RequestPath))
                return false;

            File.Delete(RequestPath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
