namespace LocalMusicHub.Models;

public sealed class LibraryStatistics
{
    public int TrackCount { get; init; }
    public int AlbumCount { get; init; }
    public int ArtistCount { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public int NeverPlayedCount { get; init; }
    public int RecentlyAddedCount { get; init; }
    public IReadOnlyList<StatCount> FormatBreakdown { get; init; } = [];
    public IReadOnlyList<StatCount> RatingBreakdown { get; init; } = [];
    public IReadOnlyList<RankedItem> TopArtistsByPlays { get; init; } = [];
    public IReadOnlyList<RankedTrack> TopTracksByPlays { get; init; } = [];
}

public sealed class StatCount
{
    public required string Label { get; init; }
    public int Count { get; init; }
}

public sealed class RankedItem
{
    public required string Name { get; init; }
    public int Count { get; init; }
}

public sealed class RankedTrack
{
    public long Id { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public int PlayCount { get; init; }
}

public sealed class DuplicateGroup
{
    public required string Key { get; init; }
    public IReadOnlyList<LibraryTrack> Tracks { get; init; } = [];
}
