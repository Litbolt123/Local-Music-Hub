using NAudio.Wave;

namespace LocalMusicHub.Services;

public sealed class GaplessSampleProvider : ISampleProvider
{
    private AudioFileReader? _current;
    private AudioFileReader? _next;
    private ISampleProvider? _currentSamples;
    private AudioFileReader? _incomingReader;
    private ISampleProvider? _incomingSamples;
    private readonly object _gate = new();

    public event EventHandler? TrackAdvanced;

    /// <summary>
    /// When false, the current track ends silently instead of switching to a preloaded next track.
    /// Used while crossfade owns the transition.
    /// </summary>
    public bool AutoAdvance { get; set; } = true;

    public GaplessSampleProvider()
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    }

    public WaveFormat WaveFormat { get; private set; }

    public AudioFileReader? CurrentReader
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>
    /// Reader whose decode position reflects audible output (incoming during crossfade tail).
    /// </summary>
    public AudioFileReader? PositionReader
    {
        get
        {
            lock (_gate)
            {
                if (_incomingReader is not null && _current is not null && ReaderAtEnd(_current))
                    return _incomingReader;
                return _current;
            }
        }
    }

    public void SetCurrent(AudioFileReader reader)
    {
        AdoptCurrent(reader);
    }

    public void AdoptCurrent(AudioFileReader reader, ISampleProvider? sampleProvider = null)
    {
        AudioFileReader? outgoing = null;
        AudioFileReader? outgoingNext = null;
        lock (_gate)
        {
            if (!ReferenceEquals(_current, reader))
                outgoing = _current;
            outgoingNext = _next;
            _current = reader;
            _next = null;
            _incomingReader = null;
            _incomingSamples = null;
            _currentSamples = sampleProvider ?? AudioFileReaders.AsSamples(reader);
            WaveFormat = reader.WaveFormat;
        }

        DisposeReadersAsync(outgoing, outgoingNext);
    }

    /// <summary>
    /// Holds the incoming track during crossfade overlap. Outgoing keeps playing until CommitIncoming.
    /// </summary>
    public void ReserveIncoming(AudioFileReader reader, ISampleProvider samples)
    {
        lock (_gate)
        {
            _incomingReader = reader;
            _incomingSamples = samples;
        }
    }

    /// <summary>
    /// Switches gapless output to the reserved incoming stream without re-wrapping samples.
    /// </summary>
    public void CommitIncoming()
    {
        AudioFileReader? outgoing = null;
        lock (_gate)
        {
            if (_incomingReader is null || _incomingSamples is null)
                return;

            if (!ReferenceEquals(_current, _incomingReader))
                outgoing = _current;

            _current = _incomingReader;
            _currentSamples = _incomingSamples;
            WaveFormat = _current.WaveFormat;
            _incomingReader = null;
            _incomingSamples = null;
            _next?.Dispose();
            _next = null;
        }

        if (outgoing is not null)
            ThreadPool.QueueUserWorkItem(_ => outgoing.Dispose());
    }

    public void PreloadNext(AudioFileReader reader)
    {
        lock (_gate)
        {
            _next?.Dispose();
            _next = reader;
        }
    }

    public void Clear()
    {
        AudioFileReader? outgoing = null;
        AudioFileReader? outgoingNext = null;
        AudioFileReader? outgoingIncoming = null;
        lock (_gate)
        {
            outgoing = _current;
            outgoingNext = _next;
            outgoingIncoming = _incomingReader;
            _current = null;
            _next = null;
            _incomingReader = null;
            _incomingSamples = null;
            _currentSamples = null;
        }

        DisposeReadersAsync(outgoing, outgoingNext, outgoingIncoming);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var advanced = false;
        AudioFileReader? outgoing = null;
        int read;
        lock (_gate)
        {
            if (_current is null || _currentSamples is null)
                return 0;

            read = _currentSamples.Read(buffer, offset, count);

            // Crossfade overlap: never return a short/zero read while outgoing has ended but incoming is reserved.
            if (!AutoAdvance && _incomingSamples is not null && read < count)
            {
                if (read > 0)
                    Array.Clear(buffer, offset + read, count - read);
                else
                    Array.Clear(buffer, offset, count);
                return count;
            }

            if (read < count && _next is not null && AutoAdvance)
            {
                var nextProvider = AudioFileReaders.AsSamples(_next);
                if (nextProvider.WaveFormat.SampleRate != WaveFormat.SampleRate ||
                    nextProvider.WaveFormat.Channels != WaveFormat.Channels)
                {
                    return read;
                }

                outgoing = _current;
                _current = _next;
                _next = null;
                _currentSamples = AudioFileReaders.AsSamples(_current);
                WaveFormat = _current.WaveFormat;
                advanced = true;
                read += _currentSamples.Read(buffer, offset + read, count - read);
            }
        }

        if (outgoing is not null)
            ThreadPool.QueueUserWorkItem(_ => outgoing.Dispose());

        if (advanced)
            TrackAdvanced?.Invoke(this, EventArgs.Empty);

        return read;
    }

    private static void DisposeReadersAsync(params AudioFileReader?[] readers)
    {
        foreach (var reader in readers)
        {
            if (reader is not null)
                ThreadPool.QueueUserWorkItem(_ => reader.Dispose());
        }
    }

    private static bool ReaderAtEnd(AudioFileReader reader) =>
        reader.Length > 0 && reader.Position >= reader.Length - reader.BlockAlign * 4;
}
