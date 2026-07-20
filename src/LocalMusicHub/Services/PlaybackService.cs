using LocalMusicHub.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LocalMusicHub.Services;

public sealed class PlaybackService : IDisposable
{
    private IWavePlayer? _output;
    private AudioFileReader? _reader;
    private ISampleProvider? _sampleProvider;
    private SmoothVolumeSampleProvider? _volumeProvider;
    private SpeedSampleProvider? _speedProvider;
    private float _replayGainLinear = 1f;
    private GaplessSampleProvider? _gapless;
    private CrossfadeSampleProvider? _crossfade;
    private readonly List<LibraryTrack> _queue = [];
    private readonly List<LibraryTrack> _baseQueue = [];
    private int _queueIndex = -1;
    private bool _isPaused;
    private long? _lastRecordedPlayId;
    private bool _shuffleEnabled;
    private PlaybackRepeatMode _repeatMode = PlaybackRepeatMode.Off;
    private double _volume = 0.85;
    private int? _cueEndMs;
    private AudioFileReader? _crossfadeNextReader;
    private volatile bool _crossfadeTransitionActive;

    public event EventHandler? StateChanged;
    public event EventHandler? TrackChanged;
    public event EventHandler? QueueChanged;
    public event EventHandler<LibraryTrack>? TrackStarted;
    public event EventHandler<TimeSpan>? PositionChanged;

    /// <summary>When set, returning true prevents auto-advance at natural track end (sleep timer).</summary>
    public Func<bool>? ShouldStopInsteadOfAdvancing { get; set; }

    public LibraryTrack? CurrentTrack => _queueIndex >= 0 && _queueIndex < _queue.Count ? _queue[_queueIndex] : null;
    public int QueueIndex => _queueIndex;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public bool IsPaused => _isPaused;
    public bool ShuffleEnabled => _shuffleEnabled;
    public PlaybackRepeatMode RepeatMode => _repeatMode;
    public TimeSpan Position
    {
        get
        {
            var reader = ActiveReader;
            if (reader is null)
                return TimeSpan.Zero;

            TimeSpan pos;
            if (CurrentTrack?.CueStartMs is int start)
                pos = reader.CurrentTime - TimeSpan.FromMilliseconds(start);
            else
                pos = reader.CurrentTime;

            if (pos < TimeSpan.Zero)
                pos = TimeSpan.Zero;

            var duration = Duration;
            if (duration > TimeSpan.Zero && pos > duration)
                return duration;

            return pos;
        }
    }

    public TimeSpan Duration
    {
        get
        {
            var track = CurrentTrack;
            if (track?.CueStartMs is int start && track.CueEndMs is int end)
                return TimeSpan.FromMilliseconds(Math.Max(0, end - start));
            if (track is not null && track.Duration > TimeSpan.Zero)
                return track.Duration;
            return ActiveReader?.TotalTime ?? TimeSpan.Zero;
        }
    }
    public double Volume => _volume;
    public IReadOnlyList<LibraryTrack> Queue => _queue;

    public bool IsInCrossfadeTransition =>
        _crossfadeTransitionActive
        || _crossfade is { IsFading: true }
        || _crossfade is { HasPendingHandoff: true };

    private AudioFileReader? ActiveReader
    {
        get
        {
            if (_gapless is null)
                return _reader;

            // Keep reporting the outgoing stream until handoff so the seek bar does not
            // jump backward to the incoming track while the current song is still shown.
            if (IsInCrossfadeTransition)
                return _gapless.CurrentReader ?? _reader;

            return _gapless.PositionReader ?? _gapless.CurrentReader ?? _reader;
        }
    }

    public void Configure(bool shuffle, PlaybackRepeatMode repeatMode)
    {
        _shuffleEnabled = shuffle;
        _repeatMode = repeatMode;
    }

    public void SetShuffle(bool enabled)
    {
        if (_shuffleEnabled == enabled)
            return;

        var current = CurrentTrack;
        var index = _queueIndex;
        _shuffleEnabled = enabled;
        RebuildPlaybackOrder(current, index);
        RaiseQueue();
        RaiseState();
    }

    public PlaybackRepeatMode CycleRepeatMode()
    {
        _repeatMode = _repeatMode switch
        {
            PlaybackRepeatMode.Off => PlaybackRepeatMode.All,
            PlaybackRepeatMode.All => PlaybackRepeatMode.One,
            _ => PlaybackRepeatMode.Off,
        };
        RaiseState();
        return _repeatMode;
    }

    public void SetRepeatMode(PlaybackRepeatMode mode)
    {
        if (_repeatMode == mode)
            return;

        _repeatMode = mode;
        RaiseState();
    }

    public void SetQueue(IReadOnlyList<LibraryTrack> tracks, int startIndex = 0)
    {
        StopInternal();
        _baseQueue.Clear();
        _baseQueue.AddRange(tracks);
        _lastRecordedPlayId = null;

        if (tracks.Count == 0)
        {
            _queue.Clear();
            _queueIndex = -1;
            RaiseQueue();
            RaiseState();
            return;
        }

        var clampedStart = Math.Clamp(startIndex, 0, tracks.Count - 1);
        var startTrack = tracks[clampedStart];
        ApplyPlaybackOrder(startTrack, clampedStart);
        if (_queueIndex >= 0)
            LoadCurrent(play: false);
        RaiseQueue();
        RaiseState();
    }

    public void PlayTrack(LibraryTrack track, IReadOnlyList<LibraryTrack>? context = null)
    {
        var list = context?.ToList() ?? [track];
        var index = list.FindIndex(t => TracksMatch(t, track));
        if (index < 0)
        {
            list = [track];
            index = 0;
        }

        SetQueue(list, index);
        Play();
    }

    public void AddToQueue(IEnumerable<LibraryTrack> tracks)
    {
        var added = false;
        foreach (var track in tracks)
        {
            _baseQueue.Add(track);
            _queue.Add(track);
            added = true;
        }

        if (!added)
            return;

        if (_queueIndex < 0 && _queue.Count > 0)
        {
            _queueIndex = 0;
            LoadCurrent(play: false);
        }

        RaiseQueue();
        RaiseState();
    }

    /// <summary>Insert tracks immediately after the current item ("Play next").</summary>
    public void InsertAfterCurrent(IEnumerable<LibraryTrack> tracks)
    {
        var list = tracks.ToList();
        if (list.Count == 0)
            return;

        var insertAt = _queueIndex < 0 ? 0 : _queueIndex + 1;
        for (var i = 0; i < list.Count; i++)
        {
            var track = list[i];
            _queue.Insert(insertAt + i, track);
            // Keep base queue consistent: append for shuffle rebuilds, order not critical for base.
            _baseQueue.Add(track);
        }

        if (_queueIndex < 0 && _queue.Count > 0)
        {
            _queueIndex = 0;
            LoadCurrent(play: false);
        }

        RaiseQueue();
        RaiseState();
    }

    public void RemoveFromQueue(int index)
    {
        if (index < 0 || index >= _queue.Count)
            return;

        var removing = _queue[index];
        var removingCurrent = index == _queueIndex;
        _queue.RemoveAt(index);
        RemoveFromBaseQueue(removing);

        if (_queue.Count == 0)
        {
            StopInternal();
            _queueIndex = -1;
            RaiseQueue();
            RaiseState();
            TrackChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (index < _queueIndex)
            _queueIndex--;
        else if (removingCurrent)
        {
            _queueIndex = Math.Min(index, _queue.Count - 1);
            LoadCurrent(play: IsPlaying);
        }

        RaiseQueue();
        RaiseState();
    }

    public void MoveInQueue(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _queue.Count || toIndex < 0 || toIndex >= _queue.Count || fromIndex == toIndex)
            return;

        var item = _queue[fromIndex];
        _queue.RemoveAt(fromIndex);
        _queue.Insert(toIndex, item);

        if (_shuffleEnabled)
        {
            var baseFrom = _baseQueue.FindIndex(t => TracksMatch(t, item));
            if (baseFrom >= 0)
            {
                _baseQueue.RemoveAt(baseFrom);
                var baseTo = Math.Clamp(toIndex, 0, _baseQueue.Count);
                _baseQueue.Insert(baseTo, item);
            }
        }

        if (_queueIndex == fromIndex)
            _queueIndex = toIndex;
        else if (fromIndex < _queueIndex && toIndex >= _queueIndex)
            _queueIndex--;
        else if (fromIndex > _queueIndex && toIndex <= _queueIndex)
            _queueIndex++;

        RaiseQueue();
    }

    public void ClearQueue(bool stopPlayback = false)
    {
        if (stopPlayback)
        {
            StopInternal();
            _queue.Clear();
            _baseQueue.Clear();
            _queueIndex = -1;
            RaiseQueue();
            RaiseState();
            TrackChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_queueIndex < 0 || _queueIndex >= _queue.Count)
        {
            _queue.Clear();
            _baseQueue.Clear();
            RaiseQueue();
            return;
        }

        var current = _queue[_queueIndex];
        _queue.Clear();
        _baseQueue.Clear();
        _queue.Add(current);
        _baseQueue.Add(current);
        _queueIndex = 0;
        RaiseQueue();
    }

    public void Play()
    {
        if (_reader is null && _queueIndex >= 0)
            LoadCurrent(play: true);
        else if (_output is not null)
        {
            _output.Play();
            _isPaused = false;
            MaybeRecordPlay();
            RaiseState();
        }
    }

    public void Pause()
    {
        _output?.Pause();
        _isPaused = true;
        RaiseState();
    }

    public void TogglePlayPause()
    {
        if (IsPlaying)
            Pause();
        else
            Play();
    }

    public void Stop()
    {
        StopInternal();
        _queueIndex = -1;
        RaiseState();
        TrackChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Next()
    {
        if (_queue.Count == 0)
            return;

        if (_queueIndex < _queue.Count - 1)
        {
            _queueIndex++;
            LoadCurrent(play: true);
            return;
        }

        if (_repeatMode == PlaybackRepeatMode.All)
        {
            _queueIndex = 0;
            LoadCurrent(play: true);
            return;
        }

        StopInternal();
        RaiseState();
    }

    public void Previous()
    {
        if (_queue.Count == 0)
            return;

        if (Position.TotalSeconds > 3)
        {
            Seek(TimeSpan.Zero);
            return;
        }

        if (_queueIndex > 0)
        {
            _queueIndex--;
            LoadCurrent(play: true);
            return;
        }

        if (_repeatMode == PlaybackRepeatMode.All && _queue.Count > 1)
        {
            _queueIndex = _queue.Count - 1;
            LoadCurrent(play: true);
            return;
        }

        Seek(TimeSpan.Zero);
    }

    public void UpdateTransitionState()
    {
        if (!IsPlaying || _isPaused)
            return;

        TryBeginCrossfadeNearEnd();
    }

    public void Seek(TimeSpan position)
    {
        var reader = ActiveReader;
        if (reader is null)
            return;

        var max = Duration.TotalSeconds;
        var relative = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, max));
        var target = relative;
        if (CurrentTrack?.CueStartMs is int startMs)
            target += TimeSpan.FromMilliseconds(startMs);
        reader.CurrentTime = target;
        PositionChanged?.Invoke(this, relative);
    }

    public bool TryAdvanceAtCueEnd()
    {
        if (_cueEndMs is null || ActiveReader is null || _isPaused || !IsPlaying)
            return false;
        if (ActiveReader.CurrentTime.TotalMilliseconds < _cueEndMs.Value - 80)
            return false;
        Next();
        return true;
    }

    public void SetVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0, 1);
        App.Settings.DefaultVolume = _volume;
        ApplyLiveVolume();
        RaiseState();
    }

    public void ReloadOutputSettings()
    {
        if (_queueIndex < 0)
            return;

        var wasPlaying = IsPlaying;
        var position = Position;
        LoadCurrent(play: false);
        if (position > TimeSpan.Zero)
            Seek(position);
        if (wasPlaying)
            Play();
    }

    public (bool WasActive, bool WasPlaying, TimeSpan Position)? ReleaseCurrentFileIfMatches(string path)
    {
        var current = CurrentTrack;
        if (current is null ||
            (!string.Equals(current.FilePath, path, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(current.AudioFilePath, path, StringComparison.OrdinalIgnoreCase)))
            return null;

        var state = (WasActive: true, WasPlaying: IsPlaying, Position: Position);
        StopInternal();
        RaiseState();
        return state;
    }

    public void ResumeAfterFileRelease(bool play, TimeSpan position)
    {
        if (_queueIndex < 0 || _queueIndex >= _queue.Count)
            return;

        LoadCurrent(play: false);
        if (position > TimeSpan.Zero && position < Duration)
            Seek(position);
        if (play)
            Play();
        else
            RaiseState();
    }

    private void LoadCurrent(bool play)
    {
        StopInternal();
        var track = CurrentTrack;
        if (track is null || !File.Exists(track.AudioFilePath))
            return;

        try
        {
            var settings = App.Settings;
            settings.DefaultVolume = _volume;
            var built = PlaybackPipeline.Build(track, settings);
            _reader = built.Reader;
            _sampleProvider = built.Provider;
            _volumeProvider = built.Volume;
            _speedProvider = built.Speed;
            _replayGainLinear = built.ReplayGainLinear;
            _gapless = built.Gapless;
            _crossfade = built.Crossfade;
            if (_gapless is not null)
                _gapless.TrackAdvanced += Gapless_OnTrackAdvanced;
            if (_crossfade is not null)
            {
                _crossfade.ConfigureTransition(
                    GetRemainingForCrossfade,
                    AcquireCrossfadeNextReader,
                    ReserveCrossfadeIncoming);
                _crossfade.CrossfadeStarted += Crossfade_OnStarted;
                _crossfade.CrossfadeCompleted += Crossfade_OnCompleted;
            }

            if (track.CueStartMs is int cueStart)
                _reader.CurrentTime = TimeSpan.FromMilliseconds(cueStart);
            _cueEndMs = track.CueEndMs;

            PreloadNextTrack();

            _output = AudioOutputFactory.Create(settings.OutputBackend, settings.OutputDeviceId);
            _output.PlaybackStopped += Output_OnPlaybackStopped;
            _output.Init(_sampleProvider);
            if (play)
            {
                _output.Play();
                _isPaused = false;
                MaybeRecordPlay();
            }
        }
        catch (Exception)
        {
            StopInternal();
        }

        TrackChanged?.Invoke(this, EventArgs.Empty);
        RaiseState();
    }

    private void PreloadNextTrack()
    {
        _crossfadeNextReader?.Dispose();
        _crossfadeNextReader = null;

        if (_queueIndex < 0 || _queueIndex >= _queue.Count - 1)
            return;

        var nextTrack = _queue[_queueIndex + 1];
        if (!File.Exists(nextTrack.AudioFilePath))
            return;

        try
        {
            if (_gapless is not null && !App.Settings.CrossfadeEnabled)
            {
                var gaplessReader = CreateReaderForTrack(nextTrack);
                _gapless.PreloadNext(gaplessReader);
            }

            if (_crossfade is not null && App.Settings.CrossfadeEnabled)
            {
                // Separate reader so gapless and crossfade can overlap without sharing file position.
                _crossfadeNextReader = CreateReaderForTrack(nextTrack);
            }
        }
        catch
        {
            /* ignore preload failures */
        }
    }

    private static AudioFileReader CreateReaderForTrack(LibraryTrack track)
    {
        var reader = new AudioFileReader(track.AudioFilePath);
        if (track.CueStartMs is int nextCueStart)
            reader.CurrentTime = TimeSpan.FromMilliseconds(nextCueStart);
        return reader;
    }

    private TimeSpan GetRemainingForCrossfade()
    {
        var duration = Duration;
        if (duration <= TimeSpan.Zero)
            return TimeSpan.Zero;

        // Always measure from the outgoing decode stream during overlap (before queue handoff).
        var reader = _gapless?.CurrentReader ?? _reader;
        if (reader is null)
            return TimeSpan.Zero;

        TimeSpan position;
        if (CurrentTrack?.CueStartMs is int start)
            position = reader.CurrentTime - TimeSpan.FromMilliseconds(start);
        else
            position = reader.CurrentTime;

        if (position < TimeSpan.Zero)
            position = TimeSpan.Zero;

        var remaining = duration - position;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private AudioFileReader? AcquireCrossfadeNextReader()
    {
        if (_crossfadeNextReader is null)
            return null;

        var reader = _crossfadeNextReader;
        _crossfadeNextReader = null;
        return reader;
    }

    private void TryBeginCrossfadeNearEnd()
    {
        if (_crossfade is null || _crossfadeNextReader is null || _crossfade.IsFading || _crossfade.HasPendingHandoff)
            return;
        if (!App.Settings.CrossfadeEnabled)
            return;

        var remaining = GetRemainingForCrossfade();
        var crossfadeWindow = TimeSpan.FromSeconds(App.Settings.CrossfadeSeconds);
        if (remaining > crossfadeWindow || remaining <= TimeSpan.Zero)
            return;

        var next = AcquireCrossfadeNextReader();
        if (next is null)
            return;

        _crossfade.BeginCrossfade(next);
    }

    private void ReserveCrossfadeIncoming(AudioFileReader reader, ISampleProvider samples) =>
        _gapless?.ReserveIncoming(reader, samples);

    private void Crossfade_OnStarted(object? sender, EventArgs e)
    {
        _crossfadeTransitionActive = true;
        RampReplayGainToNextTrack();
    }

    private void RampReplayGainToNextTrack()
    {
        if (_volumeProvider is null || _queueIndex < 0 || _queueIndex >= _queue.Count - 1)
            return;

        var nextTrack = _queue[_queueIndex + 1];
        var gainDb = PlaybackPipeline.ResolveReplayGainDb(nextTrack, App.Settings.ReplayGainMode);
        _replayGainLinear = (float)Math.Pow(10, gainDb / 20.0);

        var remaining = GetRemainingForCrossfade();
        var fadeSeconds = Math.Min(App.Settings.CrossfadeSeconds, Math.Max(remaining.TotalSeconds, 0.1));
        var sampleRate = _reader?.WaveFormat.SampleRate ?? 44100;
        var rampFrames = Math.Max(1, (int)(fadeSeconds * sampleRate));
        _volumeProvider.RampTo(_replayGainLinear * (float)_volume, rampFrames);
    }

    private void Gapless_OnTrackAdvanced(object? sender, EventArgs e) => AdvanceGaplessTrack();

    private void Crossfade_OnCompleted(object? sender, CrossfadeHandoffEventArgs e)
    {
        if (_gapless is not null)
            _gapless.CommitIncoming();
        else
            _reader?.Dispose();

        _reader = e.Reader;
        AdvanceQueueAfterCrossfade();
        _cueEndMs = CurrentTrack?.CueEndMs;
        _crossfadeTransitionActive = false;
        _speedProvider?.ResetInterpolation();

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                PreloadNextTrack();
                TrackChanged?.Invoke(this, EventArgs.Empty);
                MaybeRecordPlay();
                RaiseState();
            }
            catch
            {
                /* ignore background handoff bookkeeping */
            }
        });
    }

    private void AdvanceQueueAfterCrossfade()
    {
        if (_queueIndex < _queue.Count - 1)
            _queueIndex++;
        else if (_repeatMode == PlaybackRepeatMode.All && _queue.Count > 1)
            _queueIndex = 0;
    }

    private void AdvanceGaplessTrack()
    {
        if (_gapless?.CurrentReader is null || _queueIndex < 0 || _queue.Count == 0)
            return;

        if (_queueIndex < _queue.Count - 1)
            _queueIndex++;
        else if (_repeatMode == PlaybackRepeatMode.All && _queue.Count > 1)
            _queueIndex = 0;
        else
            return;

        _reader = _gapless.CurrentReader;
        _cueEndMs = CurrentTrack?.CueEndMs;
        _speedProvider?.ResetInterpolation();

        PreloadNextTrack();
        TrackChanged?.Invoke(this, EventArgs.Empty);
        MaybeRecordPlay();
        RaiseState();
    }

    private void MaybeRecordPlay()
    {
        var track = CurrentTrack;
        if (track is null || track.Id <= 0)
            return;
        if (_lastRecordedPlayId == track.Id)
            return;

        _lastRecordedPlayId = track.Id;
        TrackStarted?.Invoke(this, track);
    }

    private void Output_OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_isPaused || _reader is null)
            return;

        if (ShouldResumeAfterSpuriousStop())
        {
            try { _output?.Play(); } catch { /* ignore */ }
            return;
        }

        if (_repeatMode == PlaybackRepeatMode.One)
        {
            LoadCurrent(play: true);
            return;
        }

        var atLast = _queueIndex >= _queue.Count - 1;
        if (ShouldStopInsteadOfAdvancing?.Invoke() == true)
        {
            StopInternal();
            RaiseState();
            return;
        }

        if (!atLast)
        {
            _queueIndex++;
            LoadCurrent(play: true);
            return;
        }

        if (_repeatMode == PlaybackRepeatMode.All && _queue.Count > 0)
        {
            _queueIndex = 0;
            LoadCurrent(play: true);
            return;
        }

        StopInternal();
        RaiseState();
    }

    /// <summary>
    /// Wasapi can fire PlaybackStopped when the outgoing stream hits EOF during crossfade
    /// even though the incoming track is still playing — resume instead of advancing the queue.
    /// </summary>
    private bool ShouldResumeAfterSpuriousStop()
    {
        if (_crossfadeTransitionActive)
            return true;

        if (_crossfade is { IsFading: true } or { HasPendingHandoff: true })
            return true;

        var reader = ActiveReader;
        if (reader is null)
            return false;

        var total = reader.TotalTime;
        if (total <= TimeSpan.FromMilliseconds(500))
            return false;

        return reader.CurrentTime + TimeSpan.FromMilliseconds(250) < total;
    }

    private void RebuildPlaybackOrder(LibraryTrack? preserveTrack, int preferredIndex)
    {
        if (_baseQueue.Count == 0)
        {
            _queue.Clear();
            _queueIndex = -1;
            return;
        }

        ApplyPlaybackOrder(preserveTrack, preferredIndex);
    }

    private void ApplyPlaybackOrder(LibraryTrack? anchorTrack, int preferredIndex)
    {
        if (_baseQueue.Count == 0)
        {
            _queue.Clear();
            _queueIndex = -1;
            return;
        }

        if (_shuffleEnabled)
        {
            _queue.Clear();
            _queue.AddRange(ShuffledCopy(_baseQueue, anchorTrack, preferredIndex));
            _queueIndex = anchorTrack is null
                ? Math.Clamp(preferredIndex, 0, _queue.Count - 1)
                : _queue.FindIndex(t => TracksMatch(t, anchorTrack));
            if (_queueIndex < 0)
                _queueIndex = 0;
            return;
        }

        _queue.Clear();
        _queue.AddRange(_baseQueue);
        _queueIndex = anchorTrack is null
            ? Math.Clamp(preferredIndex, 0, _queue.Count - 1)
            : _queue.FindIndex(t => TracksMatch(t, anchorTrack));
        if (_queueIndex < 0)
            _queueIndex = 0;
    }

    private static List<LibraryTrack> ShuffledCopy(
        IReadOnlyList<LibraryTrack> source,
        LibraryTrack? pinTrack,
        int preferredIndex)
    {
        var list = source.ToList();
        if (list.Count <= 1)
            return list;

        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        if (pinTrack is null)
            return list;

        var pinIndex = list.FindIndex(t => TracksMatch(t, pinTrack));
        if (pinIndex < 0)
            return list;

        var target = Math.Clamp(preferredIndex, 0, list.Count - 1);
        if (pinIndex == target)
            return list;

        var pinned = list[pinIndex];
        list.RemoveAt(pinIndex);
        list.Insert(target, pinned);
        return list;
    }

    private void RemoveFromBaseQueue(LibraryTrack track)
    {
        var index = _baseQueue.FindIndex(t => TracksMatch(t, track));
        if (index >= 0)
            _baseQueue.RemoveAt(index);
    }

    private static bool TracksMatch(LibraryTrack a, LibraryTrack b) =>
        a.Id > 0 && b.Id > 0
            ? a.Id == b.Id
            : string.Equals(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);

    private void StopInternal()
    {
        if (_output is not null)
        {
            _output.PlaybackStopped -= Output_OnPlaybackStopped;
            _output.Stop();
            _output.Dispose();
            _output = null;
        }

        _crossfade?.Clear();
        if (_crossfade is not null)
        {
            _crossfade.CrossfadeStarted -= Crossfade_OnStarted;
            _crossfade.CrossfadeCompleted -= Crossfade_OnCompleted;
        }
        _gapless?.Clear();
        if (_gapless is not null)
            _gapless.TrackAdvanced -= Gapless_OnTrackAdvanced;
        _crossfadeNextReader?.Dispose();
        _crossfadeNextReader = null;
        _crossfadeTransitionActive = false;
        _reader?.Dispose();
        _reader = null;
        _sampleProvider = null;
        _volumeProvider = null;
        _speedProvider = null;
        _replayGainLinear = 1f;
        _gapless = null;
        _crossfade = null;
        _isPaused = false;
    }

    private void ApplyReplayGainForCurrentTrack()
    {
        var track = CurrentTrack;
        if (track is null)
            return;

        var gainDb = PlaybackPipeline.ResolveReplayGainDb(track, App.Settings.ReplayGainMode);
        _replayGainLinear = (float)Math.Pow(10, gainDb / 20.0);
        ApplyLiveVolume();
    }

    private void ApplyLiveVolume()
    {
        if (_volumeProvider is null)
            return;
        _volumeProvider.Volume = _replayGainLinear * (float)_volume;
    }

    private void RaiseState() => StateChanged?.Invoke(this, EventArgs.Empty);
    private void RaiseQueue() => QueueChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose() => StopInternal();
}
