namespace LocalMusicHub.Services;

public enum PlaybackRepeatMode
{
    Off,
    All,
    One,
}

public static class PlaybackRepeatModeExtensions
{
    public static PlaybackRepeatMode Parse(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "one" or "track" => PlaybackRepeatMode.One,
            "all" or "playlist" => PlaybackRepeatMode.All,
            _ => PlaybackRepeatMode.Off,
        };

    public static string ToStorageValue(this PlaybackRepeatMode mode) => mode switch
    {
        PlaybackRepeatMode.One => "one",
        PlaybackRepeatMode.All => "all",
        _ => "off",
    };
}
