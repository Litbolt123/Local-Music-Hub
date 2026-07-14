namespace LocalMusicHub.Services;

public enum LyricsQueueMode
{
    /// <summary>Skip tracks with local lyrics and previously not-found entries.</summary>
    MissingOnly,

    /// <summary>Manual album/track/playlist — force fetch and overwrite.</summary>
    Manual,

    /// <summary>Retry only paths in the not-found store.</summary>
    RetryFailed,
}
