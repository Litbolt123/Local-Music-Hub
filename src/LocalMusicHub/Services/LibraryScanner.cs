using LocalMusicHub.Data;
using LocalMusicHub.Models;

namespace LocalMusicHub.Services;

public sealed class LibraryScanner
{
    private readonly LibraryRepository _repository;

    public LibraryScanner(LibraryRepository repository) => _repository = repository;

    public async Task<ScanResult> ScanFoldersAsync(
        IEnumerable<string> roots,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        await ScanAsync(roots, progress, cancellationToken, limitMissingRemovalToRoots: true).ConfigureAwait(true);

    public async Task<ScanResult> ScanAsync(
        IEnumerable<string> roots,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool limitMissingRemovalToRoots = false)
    {
        return await Task.Run(() =>
        {
            var files = EnumerateAudioFiles(roots);
            var indexed = 0;
            var now = DateTime.UtcNow;

            for (var i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = files[i];
                progress?.Report(new ScanProgress(i + 1, files.Count, file));

                try
                {
                    var resolved = CueSheetParser.ResolveTracksForFile(file, now).ToList();
                    var hasCueVirtual = resolved.Any(t =>
                        t.FilePath.Contains(CuePathHelper.CueSuffix, StringComparison.Ordinal));
                    foreach (var track in resolved)
                    {
                        _repository.UpsertTrack(track);
                        indexed++;
                    }

                    if (hasCueVirtual)
                        _repository.RemovePath(file);
                }
                catch
                {
                    /* skip unreadable files */
                }
            }

            if (limitMissingRemovalToRoots)
                _repository.RemoveMissingPathsUnderRoots(roots, files);
            else
                _repository.RemoveMissingPaths(files);
            return new ScanResult(files.Count, indexed);
        }, cancellationToken).ConfigureAwait(true);
    }

    private static List<string> EnumerateAudioFiles(IEnumerable<string> roots)
    {
        var files = new List<string>();
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
                continue;

            try
            {
                files.AddRange(Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                    .Where(AudioTagReader.IsSupported));
            }
            catch
            {
                /* skip inaccessible folders */
            }
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToList();
    }
}

public readonly record struct ScanProgress(int Done, int Total, string CurrentFile);
public readonly record struct ScanResult(int FilesFound, int Indexed);
