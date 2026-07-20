using LocalMusicHub.Data;

namespace LocalMusicHub.Services;

/// <summary>Lazy library DB/repository — avoids blocking window construction on SQLite open.</summary>
public sealed class LibraryDataServices
{
    private readonly Lazy<LibraryDatabase> _database;
    private readonly Lazy<LibraryRepository> _repository;
    private readonly Lazy<LibraryScanner> _scanner;
    private readonly Lazy<LibraryFolderWatcher> _folderWatcher;

    public LibraryDataServices()
    {
        _database = new Lazy<LibraryDatabase>(() =>
        {
            var start = StartupProfiler.NowMs();
            var db = new LibraryDatabase(AppPaths.DatabasePath);
            StartupProfiler.MarkAfter("library.database_open", start);
            return db;
        });
        _repository = new Lazy<LibraryRepository>(() =>
        {
            var start = StartupProfiler.NowMs();
            var repo = new LibraryRepository(_database.Value);
            StartupProfiler.MarkAfter("library.repository_init", start);
            return repo;
        });
        _scanner = new Lazy<LibraryScanner>(() => new LibraryScanner(_repository.Value));
        _folderWatcher = new Lazy<LibraryFolderWatcher>(() => new LibraryFolderWatcher(_repository.Value));
    }

    public LibraryDatabase Database => _database.Value;
    public LibraryRepository Repository => _repository.Value;
    public LibraryScanner Scanner => _scanner.Value;
    public LibraryFolderWatcher FolderWatcher => _folderWatcher.Value;

    public void DisposeAll()
    {
        if (_folderWatcher.IsValueCreated)
            _folderWatcher.Value.Dispose();
        if (_database.IsValueCreated)
            _database.Value.Dispose();
    }
}
