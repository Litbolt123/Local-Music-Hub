namespace LocalMusicHub.Services;

public sealed class LibraryIngestEvent : EventArgs
{
    public required string Path { get; init; }
    public string ContentKind { get; init; } = "music";
    public string? SourceUrl { get; init; }
    public string? CompletedUtc { get; init; }
    public bool ImportFolder { get; init; }
}
