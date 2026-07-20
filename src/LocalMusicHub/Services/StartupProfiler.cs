using System.Diagnostics;
using System.Text;

namespace LocalMusicHub.Services;

/// <summary>Writes per-phase startup timings to <c>startup-timing.log</c> in the app data folder.</summary>
public static class StartupProfiler
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Gate = new();
    private static readonly List<Entry> Entries = [];
    private static long _lastMs;
    private static bool _enabled = true;
    private static int _finished;

    public static string LogPath => Path.Combine(AppPaths.DataDirectory, "startup-timing.log");

    private sealed record Entry(string Name, long TotalMs, long DeltaMs);

    public static void Configure(bool enabled) => _enabled = enabled;

    public static long NowMs() => Clock.ElapsedMilliseconds;

    public static void Mark(string name)
    {
        if (!_enabled)
            return;

        var total = Clock.ElapsedMilliseconds;
        lock (Gate)
        {
            var delta = total - _lastMs;
            _lastMs = total;
            Entries.Add(new Entry(name, total, delta));
        }
    }

    public static void MarkAfter(string name, long startMs)
    {
        var elapsed = Clock.ElapsedMilliseconds - startMs;
        Mark($"{name} [{elapsed} ms]");
    }

    public static void Finish(string reason = "startup.complete")
    {
        if (!_enabled || Interlocked.Exchange(ref _finished, 1) == 1)
            return;

        Mark(reason);

        List<Entry> snapshot;
        lock (Gate)
            snapshot = [.. Entries];

        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var sb = new StringBuilder();
            sb.AppendLine($"=== Local Music Hub startup {DateTime.Now:yyyy-MM-dd HH:mm:ss} (v{App.VersionDisplay}) ===");
            sb.AppendLine($"Log: {LogPath}");
            sb.AppendLine();
            foreach (var e in snapshot)
                sb.AppendLine($"  +{e.TotalMs,6} ms  (+{e.DeltaMs,6} ms)  {e.Name}");

            sb.AppendLine();
            sb.AppendLine("  --- slowest phases ---");
            foreach (var e in snapshot
                         .Where(x => x.DeltaMs > 0)
                         .OrderByDescending(x => x.DeltaMs)
                         .Take(15))
            {
                sb.AppendLine($"  {e.DeltaMs,6} ms  {e.Name}");
            }

            sb.AppendLine();
            File.AppendAllText(LogPath, sb.ToString());
        }
        catch
        {
            /* ignore */
        }

        Debug.WriteLine($"Startup timing log written ({snapshot.LastOrDefault()?.TotalMs} ms total)");
    }
}
