using LocalMusicHub.Data;

namespace LocalMusicHub.Services;

public sealed class LibraryFolderWatcher : IDisposable
{
    private readonly LibraryRepository _repository;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly object _pendingGate = new();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Timers.Timer _debounce = new(800) { AutoReset = false };
    private bool _enabled;

    public event EventHandler? LibraryChanged;

    public bool SuppressEvents { get; set; }

    public LibraryFolderWatcher(LibraryRepository repository)
    {
        _repository = repository;
        _debounce.Elapsed += (_, _) => FlushPending();
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
        }

        if (!deleted && !AudioTagReader.IsSupported(path))
            return;

        lock (_pendingGate)
        {
            if (deleted)
                _pending.Add("!" + path);
            else
                _pending.Add(path);
        }

        _debounce.Stop();
        _debounce.Start();
    }

    private void QueueRename(string oldPath, string newPath)
    {
        if (!_enabled || SuppressEvents || string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
            return;

        if (!AudioTagReader.IsSupported(newPath) && !AudioTagReader.IsSupported(oldPath))
            return;

        lock (_pendingGate)
            _pending.Add($"~{oldPath}|{newPath}");

        _debounce.Stop();
        _debounce.Start();
    }

    private void FlushPending()
    {
        List<string> batch;
        lock (_pendingGate)
        {
            batch = _pending.ToList();
            _pending.Clear();
        }

        var changed = false;
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

                    if (File.Exists(newPath) && AudioTagReader.IsSupported(newPath))
                    {
                        Thread.Sleep(150);
                        var track = AudioTagReader.Read(newPath, DateTime.UtcNow);
                        _repository.UpsertTrack(track);
                        changed = true;
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

                if (!File.Exists(item) || !AudioTagReader.IsSupported(item))
                    continue;

                Thread.Sleep(150);
                if (!File.Exists(item))
                    continue;

                var read = AudioTagReader.Read(item, DateTime.UtcNow);
                _repository.UpsertTrack(read);
                changed = true;
            }
            catch
            {
                /* ignore transient IO / tag errors */
            }
        }

        if (changed)
            LibraryChanged?.Invoke(this, EventArgs.Empty);
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
            _pending.Clear();
    }

    public void Dispose()
    {
        Stop();
        _debounce.Dispose();
    }
}
