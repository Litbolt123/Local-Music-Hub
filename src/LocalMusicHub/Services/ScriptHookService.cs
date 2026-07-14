using System.Diagnostics;
using System.Text.Json;
using LocalMusicHub.Models;

namespace LocalMusicHub.Services;

/// <summary>
/// Lightweight scripting hooks: optional .ps1 / .cmd / .bat files in the Scripts folder,
/// triggered on playback and library events.
/// </summary>
public sealed class ScriptHookService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public bool Enabled { get; set; }

    public static string ScriptsDirectory => Path.Combine(AppPaths.DataDirectory, "Scripts");

    public void EnsureScriptsFolder() => EnsureScriptsFolderStatic();

    public static void EnsureScriptsFolderStatic()
    {
        Directory.CreateDirectory(ScriptsDirectory);
        var readme = Path.Combine(ScriptsDirectory, "README.txt");
        if (File.Exists(readme))
            return;

        File.WriteAllText(readme, """
            Local Music Hub — script hooks
            ==============================
            Enable "Run script hooks" in Settings, then place scripts here.

            Supported names (any of .ps1 / .cmd / .bat):
              on-track-started   — when a library track begins playing
              on-track-changed  — when the now-playing track changes
              on-import         — when a file is imported into the library
              on-scan-complete  — after a library scan finishes

            Environment variables passed to scripts:
              LMH_EVENT, LMH_TITLE, LMH_ARTIST, LMH_ALBUM, LMH_PATH, LMH_TRACK_ID,
              LMH_PAYLOAD_JSON (full event JSON)

            Scripts run hidden and must finish quickly (30s timeout).
            """);
    }

    public void OnTrackStarted(LibraryTrack track) =>
        Fire("on-track-started", track);

    public void OnTrackChanged(LibraryTrack? track) =>
        Fire("on-track-changed", track);

    public void OnImport(string filePath) =>
        Fire("on-import", payload: new Dictionary<string, object?>
        {
            ["event"] = "on-import",
            ["path"] = filePath,
        });

    public void OnScanComplete(int indexed) =>
        Fire("on-scan-complete", payload: new Dictionary<string, object?>
        {
            ["event"] = "on-scan-complete",
            ["indexed"] = indexed,
        });

    private void Fire(string eventName, LibraryTrack? track = null, Dictionary<string, object?>? payload = null)
    {
        if (!Enabled)
            return;

        try
        {
            EnsureScriptsFolderStatic();
            var script = FindScript(eventName);
            if (script is null)
                return;

            payload ??= BuildTrackPayload(eventName, track);
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            _ = Task.Run(() => RunScript(script, eventName, track, json));
        }
        catch
        {
            /* never break playback for hooks */
        }
    }

    private static Dictionary<string, object?> BuildTrackPayload(string eventName, LibraryTrack? track) => new()
    {
        ["event"] = eventName,
        ["id"] = track?.Id,
        ["title"] = track?.DisplayTitle,
        ["artist"] = track?.DisplayArtist,
        ["album"] = track?.DisplayAlbum,
        ["path"] = track?.FilePath,
    };

    private static string? FindScript(string eventName)
    {
        foreach (var ext in new[] { ".ps1", ".cmd", ".bat" })
        {
            var path = Path.Combine(ScriptsDirectory, eventName + ext);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static void RunScript(string scriptPath, string eventName, LibraryTrack? track, string json)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                WorkingDirectory = ScriptsDirectory,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.Environment["LMH_EVENT"] = eventName;
            psi.Environment["LMH_TITLE"] = track?.DisplayTitle ?? "";
            psi.Environment["LMH_ARTIST"] = track?.DisplayArtist ?? "";
            psi.Environment["LMH_ALBUM"] = track?.DisplayAlbum ?? "";
            psi.Environment["LMH_PATH"] = track?.FilePath ?? "";
            psi.Environment["LMH_TRACK_ID"] = track?.Id.ToString() ?? "";
            psi.Environment["LMH_PAYLOAD_JSON"] = json;

            if (scriptPath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = "powershell.exe";
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-ExecutionPolicy");
                psi.ArgumentList.Add("Bypass");
                psi.ArgumentList.Add("-File");
                psi.ArgumentList.Add(scriptPath);
            }
            else
            {
                psi.FileName = scriptPath;
            }

            using var proc = Process.Start(psi);
            if (proc is null)
                return;
            if (!proc.WaitForExit(30_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            }
        }
        catch
        {
            /* ignore */
        }
    }
}
