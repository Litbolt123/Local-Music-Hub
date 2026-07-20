using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace LocalMusicHub.Services;

/// <summary>
/// Reads YouTube Downloader settings, watches its music output folder, and syncs API credentials.
/// </summary>
public sealed class YouTubeDownloaderBridge : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _importDebounce = new(StringComparer.OrdinalIgnoreCase);

    private const int ImportDebounceMs = 3000;

    public event EventHandler<string>? NewAudioFileDetected;

    public DownloaderLinkStatus GetStatus() => GetLinkStatus();

    public static DownloaderLinkStatus GetLinkStatus()
    {
        var settings = TryReadDownloaderSettings();
        var musicFolder = ResolveMusicFolder(settings);
        var installed = File.Exists(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YouTubeToMp3", "settings.json")) || File.Exists(AppPaths.YouTubeDownloaderSettingsPath);

        return new DownloaderLinkStatus(
            Installed: installed,
            SettingsFound: settings is not null,
            MusicFolder: musicFolder,
            ExtensionPort: settings?.BrowserExtensionPort ?? 47384,
            ExtensionEnabled: settings?.BrowserExtensionEnabled ?? false);
    }

    public void Apply(AppSettings hubSettings)
    {
        SyncApiCredentials(hubSettings);
        StopWatchers();
        if (!hubSettings.IntegrateYouTubeDownloader)
            return;

        var downloader = TryReadDownloaderSettings();
        var folder = hubSettings.YouTubeDownloaderMusicFolder
                     ?? ResolveMusicFolder(downloader)
                     ?? downloader?.MusicOutputFolder;

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        if (!hubSettings.LibraryFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            hubSettings.LibraryFolders.Add(folder);

        if (!hubSettings.WatchLibraryFolders)
            return;

        WatchFolder(folder);
    }

    private void WatchFolder(string folder)
    {
        var watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };

        watcher.Created += (_, e) => OnMaybeNewAudio(e.FullPath);
        watcher.Changed += (_, e) => OnMaybeNewAudio(e.FullPath);
        lock (_gate)
            _watchers.Add(watcher);
    }

    private void OnMaybeNewAudio(string path)
    {
        if (IsIncompleteDownloadArtifact(path))
            return;

        if (!AudioTagReader.IsSupported(path))
            return;

        var key = Path.GetFullPath(path);
        if (_importDebounce.TryGetValue(key, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }

        var cts = new CancellationTokenSource();
        _importDebounce[key] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ImportDebounceMs, cts.Token).ConfigureAwait(false);
                if (!File.Exists(path))
                    return;

                NewAudioFileDetected?.Invoke(this, path);
            }
            catch (OperationCanceledException)
            {
                /* superseded by a newer file event */
            }
            finally
            {
                if (_importDebounce.TryGetValue(key, out var current) && ReferenceEquals(current, cts))
                    _importDebounce.TryRemove(key, out _);
                cts.Dispose();
            }
        });
    }

    private static bool IsIncompleteDownloadArtifact(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Contains(".temp.", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private void StopWatchers()
    {
        lock (_gate)
        {
            foreach (var w in _watchers)
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }

            _watchers.Clear();
        }
    }

    public static void SyncApiCredentials(AppSettings hubSettings)
    {
        var downloader = TryReadDownloaderSettings();
        if (downloader is null)
            return;

        if (downloader.BrowserExtensionPort > 0)
            hubSettings.YouTubeDownloaderPort = downloader.BrowserExtensionPort;

        if (!string.IsNullOrWhiteSpace(downloader.BrowserExtensionToken))
            hubSettings.YouTubeDownloaderToken = downloader.BrowserExtensionToken;
    }

    public static string? ResolveMusicFolder(DownloaderSettingsSnapshot? settings)
    {
        if (settings is null)
            return null;

        if (!string.IsNullOrWhiteSpace(settings.MusicOutputFolder))
            return settings.MusicOutputFolder;

        return AppPaths.DefaultMusicFolder;
    }

    public static DownloaderSettingsSnapshot? TryReadDownloaderSettings()
    {
        try
        {
            if (!File.Exists(AppPaths.YouTubeDownloaderSettingsPath))
                return null;

            var json = File.ReadAllText(AppPaths.YouTubeDownloaderSettingsPath);
            return JsonSerializer.Deserialize<DownloaderSettingsSnapshot>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => StopWatchers();
}

public sealed class DownloaderSettingsSnapshot
{
    public string MusicOutputFolder { get; set; } = "";
    public bool BrowserExtensionEnabled { get; set; }
    public int BrowserExtensionPort { get; set; } = 47384;
    public string BrowserExtensionToken { get; set; } = "";
}

public readonly record struct DownloaderLinkStatus(
    bool Installed,
    bool SettingsFound,
    string? MusicFolder,
    int ExtensionPort,
    bool ExtensionEnabled);
