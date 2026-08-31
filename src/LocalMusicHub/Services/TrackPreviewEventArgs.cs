using LocalMusicHub.Models;

namespace LocalMusicHub.Services;

/// <summary>Fired as soon as the UI should show a new track — before audio load/decode finishes.</summary>
public sealed class TrackPreviewEventArgs : EventArgs
{
    public TrackPreviewEventArgs(LibraryTrack? track) => Track = track;

    public LibraryTrack? Track { get; }
}
