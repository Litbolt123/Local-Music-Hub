using System.Runtime.InteropServices;
using NAudio.Flac;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LocalMusicHub.Services;

/// <summary>
/// WaveStream + float samples with format-aware open
/// (FLAC, OGG/Vorbis, WAV, MP3 ACM fallback when Media Foundation fails).
/// </summary>
public sealed class HubAudioReader : WaveStream, ISampleProvider
{
    private readonly WaveStream _source;
    private readonly ISampleProvider _samples;
    private readonly bool _ownsSource;

    private HubAudioReader(WaveStream source, ISampleProvider samples, bool ownsSource = true)
    {
        _source = source;
        _samples = samples;
        _ownsSource = ownsSource;
        WaveFormat = samples.WaveFormat;
    }

    public override WaveFormat WaveFormat { get; }

    public override long Length => _source.Length;

    public override long Position
    {
        get => _source.Position;
        set => _source.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        _source.Read(buffer, offset, count);

    public int Read(float[] buffer, int offset, int count) =>
        _samples.Read(buffer, offset, count);

    public static HubAudioReader Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Audio file not found.", path);

        var ext = Path.GetExtension(path);
        var kind = SniffKind(path, ext);

        try
        {
            return kind switch
            {
                AudioKind.Flac => OpenFlac(path),
                AudioKind.Vorbis => FromVorbis(path),
                AudioKind.Wav => FromWav(path),
                AudioKind.Mp3 => OpenMp3(path),
                _ => FromAudioFileReader(path),
            };
        }
        catch (Exception first) when (IsUnsupportedByteStream(first) || IsFlacSyncFailure(first))
        {
            try
            {
                if (kind != AudioKind.Flac && LooksLikeFlac(path))
                    return OpenFlac(path);
                if (kind != AudioKind.Vorbis && LooksLikeOgg(path))
                    return FromVorbis(path);
                if (kind != AudioKind.Mp3 && LooksLikeMp3(path))
                    return OpenMp3(path);
                if (kind != AudioKind.Wav && LooksLikeWav(path))
                    return FromWav(path);
                if (kind == AudioKind.Flac)
                {
                    var alt = OpenBySniffAfterId3(path);
                    if (alt is not null)
                        return alt;
                }
            }
            catch
            {
                /* fall through */
            }

            throw new InvalidOperationException(FriendlyUnsupportedMessage(path, ext), first);
        }
    }

    private static HubAudioReader FromAudioFileReader(string path)
    {
        var reader = new AudioFileReader(path);
        return new HubAudioReader(reader, reader);
    }

    /// <summary>
    /// Many tagged FLACs start with ID3v2 before the fLaC sync; BunLabs FlacReader requires sync at stream start.
    /// </summary>
    private static HubAudioReader OpenFlac(string path)
    {
        Exception? managedFailure = null;
        try
        {
            var stream = File.OpenRead(path);
            try
            {
                SeekPastId3v2(stream);
                if (!SeekToFlacSync(stream))
                {
                    stream.Dispose();
                    return OpenBySniffAfterId3(path) ?? FromAudioFileReader(path);
                }

                var reader = new FlacReader(stream); // takes ownership of stream
                return new HubAudioReader(reader, reader.ToSampleProvider());
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        catch (Exception ex)
        {
            managedFailure = ex;
        }

        try
        {
            return FromAudioFileReader(path);
        }
        catch (Exception mfEx)
        {
            try
            {
                var alt = OpenBySniffAfterId3(path);
                if (alt is not null)
                    return alt;
            }
            catch
            {
                /* ignore */
            }

            throw new InvalidOperationException(
                FriendlyUnsupportedMessage(path, Path.GetExtension(path)),
                managedFailure ?? mfEx);
        }
    }

    private static HubAudioReader? OpenBySniffAfterId3(string path)
    {
        using var probe = File.OpenRead(path);
        SeekPastId3v2(probe);
        var pos = probe.Position;
        Span<byte> magic = stackalloc byte[4];
        if (probe.Read(magic) < 4)
            return null;
        probe.Position = pos;

        if (magic.SequenceEqual("fLaC"u8))
            return null; // caller already tried / should use OpenFlac
        if (magic.SequenceEqual("OggS"u8))
            return FromVorbis(path);
        if (magic.SequenceEqual("RIFF"u8))
            return FromWav(path);
        if (magic[0] == 0xFF && (magic[1] & 0xE0) == 0xE0)
            return OpenMp3(path);
        return null;
    }

    private static HubAudioReader FromVorbis(string path)
    {
        var reader = new VorbisWaveReader(path);
        return new HubAudioReader(reader, reader.ToSampleProvider());
    }

    private static HubAudioReader FromWav(string path)
    {
        var reader = new WaveFileReader(path);
        return new HubAudioReader(reader, reader.ToSampleProvider());
    }

    private static HubAudioReader OpenMp3(string path)
    {
        try
        {
            return FromAudioFileReader(path);
        }
        catch (Exception ex) when (IsUnsupportedByteStream(ex) || IsAcmMp3Failure(ex))
        {
            var reader = new Mp3FileReader(path);
            return new HubAudioReader(reader, reader.ToSampleProvider());
        }
    }

    public static bool IsUnsupportedByteStream(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is COMException { HResult: unchecked((int)0xC00D36C4) })
                return true;
            if (current.Message.Contains("0xC00D36C4", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("byte stream type", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsFlacSyncFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("fLaC", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("Invalid Flac", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsAcmMp3Failure(Exception ex) =>
        ex.Message.Contains("NoDriver", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("AcmNotPossible", StringComparison.OrdinalIgnoreCase);

    private static string FriendlyUnsupportedMessage(string path, string ext)
    {
        var name = Path.GetFileName(path);
        if (ext.Equals(".flac", StringComparison.OrdinalIgnoreCase))
            return $"Can’t play “{name}” (FLAC decode failed).";
        if (ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".oga", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".opus", StringComparison.OrdinalIgnoreCase))
            return $"Can’t play “{name}” (OGG/Opus needs a compatible decoder).";
        return $"Can’t play “{name}” — format not supported by Windows Media Foundation.";
    }

    private enum AudioKind { Unknown, Mp3, Wav, Vorbis, Flac }

    private static AudioKind SniffKind(string path, string ext)
    {
        if (ext.Equals(".flac", StringComparison.OrdinalIgnoreCase))
            return AudioKind.Flac;
        if (ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".oga", StringComparison.OrdinalIgnoreCase))
            return AudioKind.Vorbis;
        if (ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            return AudioKind.Mp3;
        if (ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".wave", StringComparison.OrdinalIgnoreCase))
            return AudioKind.Wav;

        if (LooksLikeFlac(path))
            return AudioKind.Flac;
        if (LooksLikeOgg(path))
            return AudioKind.Vorbis;
        if (LooksLikeWav(path))
            return AudioKind.Wav;
        if (LooksLikeMp3(path))
            return AudioKind.Mp3;
        return AudioKind.Unknown;
    }

    private static bool LooksLikeFlac(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            SeekPastId3v2(fs);
            return SeekToFlacSync(fs, maxScan: 16);
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeOgg(string path) => FileStartsWith(path, "OggS"u8);
    private static bool LooksLikeWav(string path) => FileStartsWith(path, "RIFF"u8);

    private static bool LooksLikeMp3(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[3];
            using var fs = File.OpenRead(path);
            if (fs.Read(header) < 3)
                return false;
            if (header[0] == (byte)'I' && header[1] == (byte)'D' && header[2] == (byte)'3')
                return true;
            return header[0] == 0xFF && (header[1] & 0xE0) == 0xE0;
        }
        catch
        {
            return false;
        }
    }

    private static bool FileStartsWith(string path, ReadOnlySpan<byte> magic)
    {
        try
        {
            Span<byte> header = stackalloc byte[magic.Length];
            using var fs = File.OpenRead(path);
            if (fs.Read(header) < magic.Length)
                return false;
            return header.SequenceEqual(magic);
        }
        catch
        {
            return false;
        }
    }

    private static void SeekPastId3v2(Stream stream)
    {
        if (!stream.CanSeek)
            return;

        var start = stream.Position;
        Span<byte> hdr = stackalloc byte[10];
        if (stream.Read(hdr) < 10)
        {
            stream.Position = start;
            return;
        }

        if (hdr[0] != (byte)'I' || hdr[1] != (byte)'D' || hdr[2] != (byte)'3')
        {
            stream.Position = start;
            return;
        }

        // Synchsafe integer size (excludes 10-byte header; optional footer adds 10).
        var size = ((hdr[6] & 0x7F) << 21) |
                   ((hdr[7] & 0x7F) << 14) |
                   ((hdr[8] & 0x7F) << 7) |
                   (hdr[9] & 0x7F);
        var footer = (hdr[5] & 0x10) != 0 ? 10 : 0;
        var next = start + 10 + size + footer;
        if (next < start || next > stream.Length)
        {
            stream.Position = start;
            return;
        }

        stream.Position = next;
    }

    /// <summary>Leaves stream positioned at the fLaC sync so FlacReader can read it.</summary>
    private static bool SeekToFlacSync(Stream stream, int maxScan = 64 * 1024)
    {
        if (!stream.CanSeek)
            return false;

        var origin = stream.Position;
        Span<byte> window = stackalloc byte[4];
        if (stream.Read(window) == 4 && window.SequenceEqual("fLaC"u8))
        {
            stream.Position = origin;
            return true;
        }

        // Scan a little for sync (junk / padding after ID3).
        stream.Position = origin;
        var limit = Math.Min(stream.Length, origin + maxScan);
        Span<byte> buf = stackalloc byte[4096];
        var carry = new byte[3];
        var carryLen = 0;

        while (stream.Position < limit)
        {
            var toRead = (int)Math.Min(buf.Length, limit - stream.Position);
            var n = stream.Read(buf[..toRead]);
            if (n <= 0)
                break;

            for (var i = 0; i < n; i++)
            {
                byte b = buf[i];
                // Match "fLaC" across buffer boundaries using a tiny carry.
                if (carryLen == 0 && b == (byte)'f')
                {
                    carry[0] = b;
                    carryLen = 1;
                }
                else if (carryLen == 1 && b == (byte)'L')
                {
                    carry[1] = b;
                    carryLen = 2;
                }
                else if (carryLen == 2 && b == (byte)'a')
                {
                    carry[2] = b;
                    carryLen = 3;
                }
                else if (carryLen == 3 && b == (byte)'C')
                {
                    var syncPos = stream.Position - (n - i) - 3;
                    stream.Position = syncPos;
                    return true;
                }
                else if (b == (byte)'f')
                {
                    carry[0] = b;
                    carryLen = 1;
                }
                else
                {
                    carryLen = 0;
                }
            }
        }

        stream.Position = origin;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsSource)
            _source.Dispose();
        base.Dispose(disposing);
    }
}
