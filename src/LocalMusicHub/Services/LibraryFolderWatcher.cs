using LocalMusicHub.Data;

namespace LocalMusicHub.Services;

public sealed class LibraryFolderWatcher : IDisposable
{
    private readonly LibraryRepository _repository;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly object _pendingGate = new();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _retryCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Timers.Timer _debounce = new(800) { AutoReset = false };
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private bool _enabled;

    public event EventHandler? LibraryChanged;

    public bool SuppressEvents { get; set; }

    public LibraryFolderWatcher(LibraryRepository repository)
    {
        _repository = repository;
        _debounce.Elapsed += (_, _) => _ = FlushPendingAsync();
    }

    public void Apply(IEnumerable<string> roots, bool enabled)
    {
        Stop();
        _enabled = enabled;
        if (!enabled)
            return;

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
                continue;

            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
                    // Default 8 KB drops events under Explorer/OneDrive copy storms.
                    InternalBufferSize = 64 * 1024,
                    EnableRaisingEvents = true,
                };
                watcher.Created += OnChanged;
                watcher.Changed += OnChanged;
                watcher.Renamed += OnRenamed;
                watcher.Deleted += OnDeleted;
                _watchers.Add(watcher);
            }
            catch
            {
                /* skip inaccessible folders */
            }
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => QueuePath(e.FullPath);
    private void OnRenamed(object sender, RenamedEventArgs e) => QueueRename(e.OldFullPath, e.FullPath);
    private void OnDeleted(object sender, FileSystemEventArgs e) => QueuePath(e.FullPath, deleted: true);

    private void QueuePath(string path, bool deleted = false)
    {
        if (!_enabled || SuppressEvents || string.IsNullOrWhiteSpace(path))
            return;

        if (!deleted)
        {
            var name = Path.GetFileName(path);
            if (name.Contains(".temp.", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // New/renamed folders: pick up audio that landed with the folder (Explorer bulk copy).
            if (Directory.Exists(path))
            {
                QueueDirectoryAudio(path);
                return;
            }

            if (!AudioTagReader.IsSupported(path))
                return;
        }

        lock (_pendingGate)
        {
            if (deleted)
            {
                _pending.Add("!" + path);
                _retryCounts.Remove(path);
            }
            else
            {
                _pending.Add(path);
            }
        }

        KickDebounce();
    }

    private void QueueDirectoryAudio(string directory)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                if (!AudioTagReader.IsSupported(file))
                    continue;
                lock (_pendingGate)
                    _pending.Add(file);
            }
        }
        catch
        {
            /* folder may still be filling in */
        }

        lock (_pendingGate)
        {
            if (_pending.Count == 0)
                return;
        }

        KickDebounce();
    }

    private void QueueRename(string oldPath, string newPath)
    {
        if (!_enabled || SuppressEvents || string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
            return;

        if (Directory.Exists(newPath))
        {
            QueueDirectoryAudio(newPath);
            return;
        }

        if (!AudioTagReader.IsSupported(newPath) && !AudioTagReader.IsSupported(oldPath))
            return;

        lock (_pendingGate)
            _pending.Add($"~{oldPath}|{newPath}");

        KickDebounce();
    }

    private void KickDebounce(double intervalMs = 800)
    {
        _debounce.Stop();
        _debounce.Interval = intervalMs;
        _debounce.Start();
    }

    private async Task FlushPendingAsync()
    {
        if (!await _flushGate.WaitAsync(0).ConfigureAwait(false))
        {
            KickDebounce(1200);
            return;
        }

        try
        {
            List<string> batch;
            lock (_pendingGate)
            {
                batch = _pending.ToList();
                _pending.Clear();
            }

            var changed = false;
            var retryLater = false;

            foreach (var item in batch)
            {
                try
                {
                    if (item.StartsWith('~'))
                    {
                        var parts = item[1..].Split('|', 2);
                        if (parts.Length != 2)
                            continue;

                        var oldPath = parts[0];
                        var newPath = parts[1];
                        if (_repository.MigrateFilePath(oldPath, newPath))
                        {
                            changed = true;
                            continue;
                        }

                        if (await TryIndexFileAsync(newPath).ConfigureAwait(false))
                        {
                            ClearRetry(newPath);
                            changed = true;
                        }
                        else
                        {
                            retryLater |= ScheduleRetry(item, newPath);
                        }

                        continue;
                    }

                    if (item.StartsWith('!'))
                    {
                        var path = item[1..];
                        _repository.RemovePath(path);
                        changed = true;
                        continue;
                    }

                    if (await TryIndexFileAsync(item).ConfigureAwait(false))
                    {
                        ClearRetry(item);
                        changed = true;
                    }
                    else
                    {
                        retryLater |= ScheduleRetry(item, item);
                    }
                }
                catch
                {
                    if (!item.StartsWith('!'))
                    {
                        var key = item.StartsWith('~') ? item.Split('|').LastOrDefault() ?? item : item;
                        retryLater |= ScheduleRetry(item, key);
                    }
                }
            }

            if (retryLater)
                KickDebounce(2500);

            if (changed)
                LibraryChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private async Task<bool> TryIndexFileAsync(string path)
    {
        if (!File.Exists(path) || !AudioTagReader.IsSupported(path))
            return false;

        var track = await AudioFileAccess.ReadTrackWhenReadyAsync(path, DateTime.UtcNow)
            .ConfigureAwait(false);
        if (track is null)
            return false;

        _repository.UpsertTrack(track);
        return true;
    }

    private void ClearRetry(string path)
    {
        lock (_pendingGate)
            _retryCounts.Remove(path);
    }

    private bool ScheduleRetry(string pendingKey, string pathForCount)
    {
        lock (_pendingGate)
        {
            _retryCounts.TryGetValue(pathForCount, out var count);
            count++;
            if (count > 3)
            {
                _retryCounts.Remove(pathForCount);
                return false;
            }

            _retryCounts[pathForCount] = count;
            _pending.Add(pendingKey);
            return true;
        }
    }

    private void Stop()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
        lock (_pendingGate)
        {
            _pending.Clear();
            _retryCounts.Clear();
        }
    }

    public void Dispose()
    {
        Stop();
        _debounce.Dispose();
        _flushGate.Dispose();
    }
}
