using System.ComponentModel;
using System.Runtime.CompilerServices;
using LocalMusicHub.Services;

namespace LocalMusicHub.Models;

public sealed class LibraryTrack
{
    public long Id { get; init; }
    public required string FilePath { get; init; }
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public string AlbumArtist { get; init; } = "";
    public int? TrackNumber { get; init; }
    public int? Year { get; init; }
    public string Genre { get; init; } = "";
    public TimeSpan Duration { get; init; }
    public int Bitrate { get; init; }
    public string Format { get; init; } = "";
    public DateTime DateAddedUtc { get; init; }
    public DateTime FileModifiedUtc { get; init; }
    public byte[]? CoverArt { get; init; }
    public int PlayCount { get; init; }
    public DateTime? LastPlayedUtc { get; init; }
    public int Rating { get; init; }
    public double? ReplayGainTrackDb { get; init; }
    public double? ReplayGainAlbumDb { get; init; }
    public double? ReplayGainTrackPeak { get; init; }
    public double? ReplayGainAlbumPeak { get; init; }
    public int? CueStartMs { get; init; }
    public int? CueEndMs { get; init; }
    public string ReviewStatus { get; set; } = "none";

    public string AudioFilePath => CuePathHelper.ResolveAudioPath(FilePath);

    public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? "Unknown Artist" : Artist;
    public string DisplayAlbum => string.IsNullOrWhiteSpace(Album) ? "Unknown Album" : Album;
    public string DisplayAlbumArtist => string.IsNullOrWhiteSpace(AlbumArtist) ? DisplayArtist : AlbumArtist;
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? System.IO.Path.GetFileNameWithoutExtension(FilePath) : Title;
    public string ListLabel => $"{DisplayTitle}  ·  {DisplayArtist}";
    public string DurationLabel => Duration.TotalHours >= 1
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"m\:ss");
    public string PlayStatsLabel => PlayCount <= 0
        ? "Never played"
        : LastPlayedUtc is null
            ? $"{PlayCount} plays"
            : $"{PlayCount} plays · last {LastPlayedUtc.Value.ToLocalTime():g}";
    public string RatingLabel => Rating <= 0 ? "—" : new string('★', Math.Clamp(Rating, 1, 5));
}

public sealed class ArtistLibraryStats
{
    public int TrackCount { get; init; }
    public int PlayCount { get; init; }
    public int AlbumCount { get; init; }
}

public sealed class LibraryAlbum : INotifyPropertyChanged
{
    public required string Key { get; init; }
    public required string Album { get; init; }
    public required string AlbumArtist { get; init; }
    public int TrackCount { get; init; }
    public int? Year { get; init; }
    public byte[]? CoverArt { get; init; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    /// <summary>True only for the album that is currently playing (not paused) — other albums stay Play.</summary>
    private bool _showPause;
    public bool ShowPause
    {
        get => _showPause;
        set
        {
            if (_showPause == value)
                return;
            _showPause = value;
            OnPropertyChanged();
        }
    }

    public string DisplayLabel => $"{AlbumArtist} — {Album}";
    public string YearLabel => Year is > 0 ? Year.Value.ToString() : "";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class LibraryArtist
{
    public required string Name { get; init; }
    public int TrackCount { get; init; }
    public int AlbumCount { get; init; }
}

public sealed class LibraryGenre
{
    public required string Name { get; init; }
    public int TrackCount { get; init; }
    public int AlbumCount { get; init; }
    public string Subtitle =>
        $"{TrackCount} track{(TrackCount == 1 ? "" : "s")} · {AlbumCount} album{(AlbumCount == 1 ? "" : "s")}";
}

public sealed class LibraryPlaylist
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public int TrackCount { get; init; }
    public bool IsSmart { get; init; }
    public SmartPlaylistRules Rules { get; init; } = new();

    public string NavLabel => IsSmart ? $"⚡ {Name}" : Name;
    public byte[]? CoverArt { get; init; }
    public long? FolderId { get; init; }
}

public sealed class PlaylistFolder
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public long? ParentId { get; init; }
}

public sealed class PlaylistTreeNode
{
    public bool IsFolder { get; init; }
    public long? FolderId { get; init; }
    public LibraryPlaylist? Playlist { get; init; }
    public string Name { get; init; } = "";
    public List<PlaylistTreeNode> Children { get; } = [];
    public string NavLabel => IsFolder ? Name : Playlist?.NavLabel ?? Name;
    public int TrackCount => Playlist?.TrackCount ?? 0;
    public byte[]? CoverArt => Playlist?.CoverArt;
}
