using System.Threading.Channels;
using LocalMusicHub.Models;

namespace LocalMusicHub.Services;

public readonly record struct LyricsPrefetchProgress(
    int Total,
    int Done,
    int Saved,
    int AlreadyHad,
    int NotFound,
    int SkippedNotFound,
    string? CurrentTitle,
    bool IsIdle);

internal sealed record LyricsWorkItem(LibraryTrack Track, bool Force);

public sealed class LyricsPrefetchService : IDisposable
{
    private readonly Channel<LyricsWorkItem> _channel = Channel.CreateUnbounded<LyricsWorkItem>();
    private readonly HashSet<string> _queuedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _queueGate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private readonly List<string> _pendingJobPaths = [];
    private readonly object _jobGate = new();

    private int _jobTotal;
    private int _jobDone;
    private int _jobSaved;
    private int _jobAlreadyHad;
    private int _jobNotFound;
    private int _jobSkippedNotFound;
    private bool _jobActive;

    public bool Enabled { get; set; } = true;

    public event EventHandler<LyricsPrefetchProgress>? ProgressChanged;

    public LyricsPrefetchService()
    {
        _worker = Task.Run(WorkerLoopAsync);
        TryResumeSavedJob();
    }

    public void Enqueue(LibraryTrack track)
    {
        if (!Enabled || !App.Settings.AutoDownloadLyrics)
            return;
        if (LyricsService.HasLocalLyrics(track.FilePath) || LyricsNotFoundStore.IsMarked(track.FilePath))
            return;
        EnqueueInternal(track, isJobTrack: false);
    }

    public void EnqueueMany(IEnumerable<LibraryTrack> tracks)
    {
        if (!Enabled || !App.Settings.AutoDownloadLyrics)
            return;
        foreach (var track in tracks)
        {
            if (LyricsService.HasLocalLyrics(track.FilePath) || LyricsNotFoundStore.IsMarked(track.FilePath))
                continue;
            EnqueueInternal(track, isJobTrack: false);
        }
    }

    public int QueueDownload(IReadOnlyList<LibraryTrack> tracks, LyricsQueueMode mode = LyricsQueueMode.MissingOnly)
    {
        var queued = 0;
        var skippedNotFound = 0;
        var force = mode is LyricsQueueMode.Manual or LyricsQueueMode.RetryFailed;
        BeginJob();

        foreach (var track in tracks)
        {
            switch (mode)
            {
                case LyricsQueueMode.MissingOnly:
                    if (LyricsService.HasLocalLyrics(track.FilePath))
                    {
                        Interlocked.Increment(ref _jobAlreadyHad);
                        continue;
                    }
                    if (LyricsNotFoundStore.IsMarked(track.FilePath))
                    {
                        Interlocked.Increment(ref _jobSkippedNotFound);
                        skippedNotFound++;
                        continue;
                    }
                    break;
                case LyricsQueueMode.RetryFailed:
                    if (!LyricsNotFoundStore.IsMarked(track.FilePath))
                        continue;
                    LyricsNotFoundStore.Clear(track.FilePath);
                    break;
                case LyricsQueueMode.Manual:
                    LyricsNotFoundStore.Clear(track.FilePath);
                    break;
            }

            if (EnqueueInternal(track, isJobTrack: true, force))
                queued++;
        }

        if (queued == 0)
            RaiseProgress(isIdle: true);
        else
            RaiseProgress();

        return queued;
    }

    private void BeginJob()
    {
        lock (_jobGate)
        {
            _jobTotal = 0;
            _jobDone = 0;
            _jobSaved = 0;
            _jobAlreadyHad = 0;
            _jobNotFound = 0;
            _jobSkippedNotFound = 0;
            _pendingJobPaths.Clear();
            _jobActive = true;
        }
    }

    private bool EnqueueInternal(LibraryTrack track, bool isJobTrack, bool force = false)
    {
        lock (_queueGate)
        {
            if (!_queuedPaths.Add(track.FilePath))
                return false;
        }

        if (isJobTrack)
        {
            lock (_jobGate)
            {
                _jobTotal++;
                _pendingJobPaths.Add(track.FilePath);
                FlushJobSnapshotLocked();
            }
        }

        _channel.Writer.TryWrite(new LyricsWorkItem(track, force));
        return true;
    }

    private async Task WorkerLoopAsync()
    {
        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                var track = item.Track;
                var force = item.Force;

                RaiseProgress(track.Title);

                if (!force && LyricsService.HasLocalLyrics(track.FilePath))
                {
                    Interlocked.Increment(ref _jobAlreadyHad);
                    CompleteJobTrack(track.FilePath);
                    continue;
                }

                try
                {
                    await Task.Delay(1100, _cts.Token).ConfigureAwait(false);
                    var ok = await LyricsService.PrefetchAsync(track, force, _cts.Token).ConfigureAwait(false);
                    if (ok)
                        Interlocked.Increment(ref _jobSaved);
                    else
                        Interlocked.Increment(ref _jobNotFound);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    Interlocked.Increment(ref _jobNotFound);
                }
                finally
                {
                    CompleteJobTrack(track.FilePath);
                }
            }
        }
        catch (OperationCanceledException)
        {
            /* shutting down */
        }
        finally
        {
            lock (_queueGate)
                _queuedPaths.Clear();
            EndJobIfIdle();
        }
    }

    private void CompleteJobTrack(string path)
    {
        Interlocked.Increment(ref _jobDone);
        lock (_queueGate)
            _queuedPaths.Remove(path);

        lock (_jobGate)
        {
            _pendingJobPaths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            if (_pendingJobPaths.Count == 0 && _jobActive)
            {
                _jobActive = false;
                LyricsJobStore.Clear();
            }
            else
                FlushJobSnapshotLocked();
        }

        RaiseProgress();
    }

    private void EndJobIfIdle()
    {
        lock (_jobGate)
        {
            if (_pendingJobPaths.Count > 0)
                return;
            _jobActive = false;
            LyricsJobStore.Clear();
        }

        RaiseProgress(isIdle: true);
    }

    private void FlushJobSnapshotLocked()
    {
        if (!_jobActive || _pendingJobPaths.Count == 0)
            return;

        LyricsJobStore.Save(new LyricsJobSnapshot
        {
            PendingPaths = _pendingJobPaths.ToList(),
            JobTotal = _jobTotal,
            JobDone = _jobDone,
            JobSaved = _jobSaved,
            JobAlreadyHad = _jobAlreadyHad,
            JobNotFound = _jobNotFound,
            JobSkippedNotFound = _jobSkippedNotFound,
        });
    }

    private void TryResumeSavedJob()
    {
        var snapshot = LyricsJobStore.Load();
        if (snapshot is null || snapshot.PendingPaths.Count == 0)
            return;

        lock (_jobGate)
        {
            _jobTotal = snapshot.JobTotal;
            _jobDone = snapshot.JobDone;
            _jobSaved = snapshot.JobSaved;
            _jobAlreadyHad = snapshot.JobAlreadyHad;
            _jobNotFound = snapshot.JobNotFound;
            _jobSkippedNotFound = snapshot.JobSkippedNotFound;
            _pendingJobPaths.Clear();
            _pendingJobPaths.AddRange(snapshot.PendingPaths);
            _jobActive = true;
        }

        foreach (var path in snapshot.PendingPaths)
        {
            if (!File.Exists(CuePathHelper.ResolveAudioPath(path)))
            {
                CompleteJobTrack(path);
                continue;
            }

            var track = new LibraryTrack { FilePath = path, Title = Path.GetFileNameWithoutExtension(path) };
            EnqueueInternal(track, isJobTrack: true);
        }

        RaiseProgress();
    }

    private void RaiseProgress(string? currentTitle = null, bool? isIdle = null)
    {
        var total = Volatile.Read(ref _jobTotal);
        var done = Volatile.Read(ref _jobDone);
        var idle = isIdle ?? (total > 0 && done >= total);
        var progress = new LyricsPrefetchProgress(
            total,
            done,
            Volatile.Read(ref _jobSaved),
            Volatile.Read(ref _jobAlreadyHad),
            Volatile.Read(ref _jobNotFound),
            Volatile.Read(ref _jobSkippedNotFound),
            currentTitle,
            idle);
        ProgressChanged?.Invoke(this, progress);
    }

    public void Dispose()
    {
        lock (_jobGate)
            FlushJobSnapshotLocked();
        _cts.Cancel();
        _channel.Writer.TryComplete();
        try
        {
            _worker.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            /* ignore */
        }

        _cts.Dispose();
    }
}
