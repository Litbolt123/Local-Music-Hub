using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalMusicHub.Models;

namespace LocalMusicHub.Services;

/// <summary>Decoded cover bitmaps keyed by track/album so lite queue loads still hit cache.</summary>
public static class CoverArtCache
{
    private const int MaxEntries = 220;
    private static readonly ConcurrentDictionary<string, BitmapSource> Cache = new();
    private static readonly ConcurrentDictionary<long, byte[]> TrackBytes = new();
    private static readonly ConcurrentDictionary<string, byte[]> AlbumBytes = new(StringComparer.Ordinal);

    public static BitmapSource? Get(LibraryTrack track, int width)
    {
        Remember(track);
        if (TryGetByTrack(track.Id, width) is { } byId)
            return byId;

        if (TryGetByAlbum(track.Album, width) is { } byAlbum)
            return byAlbum;

        return Get(track.Id, track.CoverArt, width);
    }

    public static BitmapSource? Get(long trackId, byte[]? coverArt, int width)
    {
        if (TryGetByTrack(trackId, width) is { } byId)
            return byId;

        if (coverArt is not { Length: > 0 })
            return null;

        var key = MakeBlobKey(trackId, coverArt, width);
        return Cache.TryGetValue(key, out var cached) ? cached : null;
    }

    public static BitmapSource? GetByAlbum(string? album, int width)
    {
        var key = NormalizeAlbum(album);
        if (string.IsNullOrEmpty(key))
            return null;
        return Cache.TryGetValue(MakeAlbumKey(key, width), out var cached) ? cached : null;
    }

    public static BitmapSource? GetOrDecode(LibraryTrack track, int width)
    {
        var existing = Get(track, width);
        if (existing is not null)
            return existing;

        var decoded = CoverArtHelper.ToBitmap(track.CoverArt, width, centerCropSquare: true);
        if (decoded is not null && track.CoverArt is { } cover)
            Store(track, cover, width, decoded);
        return decoded;
    }

    public static void Remember(LibraryTrack? track)
    {
        if (track is null)
            return;
        if (track.CoverArt is { Length: > 0 })
            StoreBytes(track.Id, track.Album, track.CoverArt);
    }

    public static void RememberAlbum(LibraryAlbum? album)
    {
        if (album is null)
            return;

        if (album.CoverArt is { Length: > 0 })
            StoreBytes(0, album.Album, album.CoverArt);

        if (album.CoverThumbnail is BitmapSource bmp)
            StoreAlbumBitmap(album.Album, 128, bmp);
    }

    public static void RememberCover(long trackId, string? album, byte[] coverArt) =>
        StoreBytes(trackId, album, coverArt);

    public static BitmapSource? WarmSync(LibraryTrack track, int width)
    {
        Remember(track);
        var existing = Get(track, width);
        if (existing is not null)
            return existing;

        var bytes = ResolveBytes(track);
        if (bytes is not { Length: > 0 })
            return null;

        var decoded = CoverArtHelper.ToBitmap(bytes, width, centerCropSquare: true);
        if (decoded is null)
            return null;

        Store(track, bytes, width, decoded);
        return decoded;
    }

    public static void Warm(LibraryTrack? track, int width = 288)
    {
        if (track is null)
            return;

        Remember(track);
        if (Get(track, width) is not null)
            return;

        var bytes = ResolveBytes(track);
        if (bytes is not { Length: > 0 })
            return;

        _ = Task.Run(() =>
        {
            var decoded = CoverArtHelper.ToBitmap(bytes, width, centerCropSquare: true);
            if (decoded is not null)
                Store(track, bytes, width, decoded);
        });
    }

    public static void WarmAlbum(LibraryAlbum? album, int width = 128)
    {
        if (album is null)
            return;

        RememberAlbum(album);
        if (GetByAlbum(album.Album, width) is not null)
            return;

        if (album.CoverThumbnail is BitmapSource thumb)
        {
            StoreAlbumBitmap(album.Album, width, thumb);
            return;
        }

        if (album.CoverArt is not { Length: > 0 })
            return;

        var albumName = album.Album;
        var cover = album.CoverArt;
        _ = Task.Run(() =>
        {
            var decoded = CoverArtHelper.ToBitmap(cover, width, centerCropSquare: true);
            if (decoded is not null)
                StoreAlbumBitmap(albumName, width, decoded);
        });
    }

    public static void GetOrCreateAsync(LibraryTrack track, int width, Action<BitmapSource?> onReady)
    {
        Remember(track);
        var cached = Get(track, width);
        if (cached is not null)
        {
            onReady(cached);
            return;
        }

        var bytes = ResolveBytes(track);
        if (bytes is not { Length: > 0 })
        {
            onReady(null);
            return;
        }

        _ = Task.Run(() =>
        {
            var decoded = CoverArtHelper.ToBitmap(bytes, width, centerCropSquare: true);
            if (decoded is not null)
                Store(track, bytes, width, decoded);
            return decoded;
        }).ContinueWith(
            t => onReady(t.IsCompletedSuccessfully ? t.Result : null),
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static byte[]? ResolveBytes(LibraryTrack track)
    {
        if (track.CoverArt is { Length: > 0 })
            return track.CoverArt;
        if (TrackBytes.TryGetValue(track.Id, out var byId))
            return byId;
        var album = NormalizeAlbum(track.Album);
        return !string.IsNullOrEmpty(album) && AlbumBytes.TryGetValue(album, out var byAlbum)
            ? byAlbum
            : null;
    }

    private static void Store(LibraryTrack track, byte[] coverArt, int width, BitmapSource bitmap)
    {
        StoreBytes(track.Id, track.Album, coverArt);
        Cache[MakeTrackKey(track.Id, width)] = bitmap;
        Cache[MakeBlobKey(track.Id, coverArt, width)] = bitmap;
        StoreAlbumBitmap(track.Album, width, bitmap);
        TrimIfNeeded();
    }

    private static void StoreBytes(long trackId, string? album, byte[] coverArt)
    {
        if (coverArt.Length == 0)
            return;
        if (trackId > 0)
            TrackBytes[trackId] = coverArt;
        var albumKey = NormalizeAlbum(album);
        if (!string.IsNullOrEmpty(albumKey))
            AlbumBytes[albumKey] = coverArt;
    }

    private static void StoreAlbumBitmap(string? album, int width, BitmapSource bitmap)
    {
        var key = NormalizeAlbum(album);
        if (string.IsNullOrEmpty(key))
            return;
        Cache[MakeAlbumKey(key, width)] = bitmap;
        TrimIfNeeded();
    }

    private static BitmapSource? TryGetByTrack(long trackId, int width) =>
        trackId > 0 && Cache.TryGetValue(MakeTrackKey(trackId, width), out var cached) ? cached : null;

    private static BitmapSource? TryGetByAlbum(string? album, int width)
    {
        var key = NormalizeAlbum(album);
        return string.IsNullOrEmpty(key) ? null : GetByAlbum(key, width);
    }

    private static string MakeTrackKey(long trackId, int width) => $"t:{trackId}:{width}";
    private static string MakeAlbumKey(string album, int width) => $"a:{album}:{width}";
    private static string MakeBlobKey(long trackId, byte[] coverArt, int width) =>
        $"{trackId}:{width}:{coverArt.Length}:{coverArt[0]}:{coverArt[^1]}";

    private static string NormalizeAlbum(string? album) =>
        string.IsNullOrWhiteSpace(album) ? "" : album.Trim().ToLowerInvariant();

    private static void TrimIfNeeded()
    {
        if (Cache.Count <= MaxEntries)
            return;

        foreach (var key in Cache.Keys.Take(Cache.Count - MaxEntries))
            Cache.TryRemove(key, out _);
    }
}
