using System.Collections.Concurrent;
using LocalMusicHub.Data;
using LocalMusicHub.Models;

namespace LocalMusicHub.Services;

/// <summary>Background warm-up for now-playing chrome: first tracks, cover bytes, queue neighbors.</summary>
public sealed class NowPlayingPrefetchService
{
    private readonly LibraryRepository _repository;
    private readonly ConcurrentDictionary<string, LibraryTrack> _firstTrackByAlbum = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _albumWarmInFlight = new(StringComparer.Ordinal);

    public NowPlayingPrefetchService(LibraryRepository repository) => _repository = repository;

    public LibraryTrack? TryGetFirstTrack(string album)
    {
        var key = LibraryRepository.NormalizeAlbumKey(album);
        return _firstTrackByAlbum.TryGetValue(key, out var track) ? track : null;
    }

    public LibraryTrack? GetOrLoadFirstTrack(string album)
    {
        var cached = TryGetFirstTrack(album);
        if (cached is not null)
            return cached;

        var first = _repository.GetFirstTrackForAlbum(album);
        if (first is null)
            return null;

        _firstTrackByAlbum[LibraryRepository.NormalizeAlbumKey(album)] = first;
        RememberTrackCover(first);
        CoverArtCache.Warm(first, 128);
        return first;
    }

    public void WarmAlbums(IEnumerable<LibraryAlbum> albums, int barCoverPixels = 128)
    {
        foreach (var album in albums.Take(32))
            WarmAlbum(album, barCoverPixels);
    }

    public void WarmAlbum(LibraryAlbum album, int barCoverPixels = 128)
    {
        if (!_albumWarmInFlight.TryAdd(album.Key, 0))
            return;

        _ = Task.Run(() =>
        {
            try
            {
                CoverArtCache.RememberAlbum(album);
                CoverArtCache.WarmAlbum(album, barCoverPixels);

                if (!_firstTrackByAlbum.ContainsKey(album.Key))
                {
                    var first = _repository.GetFirstTrackForAlbum(album.Album);
                    if (first is not null)
                    {
                        _firstTrackByAlbum[album.Key] = first;
                        RememberTrackCover(first);
                        CoverArtCache.Warm(first, barCoverPixels);
                    }
                }

                var cover = album.CoverArt ?? _repository.GetCoverArtForAlbum(album.Album);
                if (cover is { Length: > 0 })
                    CoverArtCache.RememberCover(0, album.Album, cover);
            }
            finally
            {
                _albumWarmInFlight.TryRemove(album.Key, out _);
            }
        });
    }

    public void WarmQueue(IReadOnlyList<LibraryTrack> tracks, int centerIndex, int barCoverPixels = 128)
    {
        if (tracks.Count == 0)
            return;

        var snapshot = tracks;
        var index = centerIndex;
        _ = Task.Run(() =>
        {
            var from = Math.Max(0, index - 2);
            var to = Math.Min(snapshot.Count - 1, index + 4);
            for (var i = from; i <= to; i++)
            {
                var track = snapshot[i];
                RememberTrackCover(track);
                CoverArtCache.Warm(track, barCoverPixels);
            }
        });
    }

    private void RememberTrackCover(LibraryTrack track)
    {
        if (track.CoverArt is { Length: > 0 })
        {
            CoverArtCache.RememberCover(track.Id, track.Album, track.CoverArt);
            return;
        }

        if (track.Id <= 0)
            return;

        var bytes = _repository.GetCoverArtForTrack(track.Id)
                    ?? _repository.GetCoverArtForAlbum(track.Album);
        if (bytes is { Length: > 0 })
            CoverArtCache.RememberCover(track.Id, track.Album, bytes);
    }
}
