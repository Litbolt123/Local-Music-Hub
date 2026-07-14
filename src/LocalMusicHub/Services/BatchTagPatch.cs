namespace LocalMusicHub.Services;

using LocalMusicHub.Models;

public sealed class BatchTagPatch
{
    public bool ApplyTitle { get; init; }
    public string Title { get; init; } = "";

    public bool ApplyArtist { get; init; }
    public string Artist { get; init; } = "";

    public bool ApplyAlbumArtist { get; init; }
    public string AlbumArtist { get; init; } = "";

    public bool ApplyAlbum { get; init; }
    public string Album { get; init; } = "";

    public bool ApplyTrackNumber { get; init; }
    public int? TrackNumber { get; init; }

    public bool ApplyYear { get; init; }
    public int? Year { get; init; }

    public bool ApplyGenre { get; init; }
    public string Genre { get; init; } = "";

    public bool ApplyRating { get; init; }
    public int Rating { get; init; }

    public bool HasFileTagChanges =>
        ApplyTitle || ApplyArtist || ApplyAlbumArtist || ApplyAlbum ||
        ApplyTrackNumber || ApplyYear || ApplyGenre;

    public LibraryTrack ApplyTo(LibraryTrack source) => new()
    {
        Id = source.Id,
        FilePath = source.FilePath,
        Title = ApplyTitle ? Title.Trim() : source.Title,
        Artist = ApplyArtist ? Artist.Trim() : source.Artist,
        AlbumArtist = ApplyAlbumArtist ? AlbumArtist.Trim() : source.AlbumArtist,
        Album = ApplyAlbum ? Album.Trim() : source.Album,
        TrackNumber = ApplyTrackNumber ? TrackNumber : source.TrackNumber,
        Year = ApplyYear ? Year : source.Year,
        Genre = ApplyGenre ? Genre.Trim() : source.Genre,
        Duration = source.Duration,
        Bitrate = source.Bitrate,
        Format = source.Format,
        DateAddedUtc = source.DateAddedUtc,
        FileModifiedUtc = DateTime.UtcNow,
        CoverArt = source.CoverArt,
        PlayCount = source.PlayCount,
        LastPlayedUtc = source.LastPlayedUtc,
        Rating = ApplyRating ? Rating : source.Rating,
    };
}
