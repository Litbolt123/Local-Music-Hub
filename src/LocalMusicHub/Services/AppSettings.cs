namespace LocalMusicHub.Services;

public sealed class AppSettings
{
    public bool UseDarkTheme { get; set; } = true;
    /// <summary>Accent palette: "purple" (classic) or "spotify" (green).</summary>
    public string AccentTheme { get; set; } = "purple";
    public List<string> LibraryFolders { get; set; } = [];
    public bool WatchLibraryFolders { get; set; } = true;
    public bool IntegrateYouTubeDownloader { get; set; } = true;
    public bool ShowYouTubeDownloaderSidebar { get; set; } = true;
    public bool YouTubeDownloaderSidebarCollapsed { get; set; }
    public string? YouTubeDownloaderMusicFolder { get; set; }
    public int YouTubeDownloaderPort { get; set; } = 47384;
    public string? YouTubeDownloaderToken { get; set; }
    public double DefaultVolume { get; set; } = 0.85;
    /// <summary>Playback speed multiplier (0.5–2.0). Pitch shifts with speed.</summary>
    public double PlaybackSpeed { get; set; } = 1.0;
    public bool Shuffle { get; set; }
    public string RepeatMode { get; set; } = "off";
    public bool MinimizeToTray { get; set; }
    public bool StartWithWindows { get; set; }
    public bool RescanLibraryOnSave { get; set; } = false;
    public string OrganizeTemplate { get; set; } = @"{album_artist}\{album}\{track:00} - {title}";
    public string? OrganizeRoot { get; set; }
    public string OutputBackend { get; set; } = "waveout";
    public string? OutputDeviceId { get; set; }
    public string ReplayGainMode { get; set; } = "off";
    public string EqPreset { get; set; } = "flat";
    public bool GaplessEnabled { get; set; }
    public bool CrossfadeEnabled { get; set; }
    public int CrossfadeSeconds { get; set; } = 6;
    public bool DiscordRichPresenceEnabled { get; set; }
    public string? DiscordClientId { get; set; }
    public bool LastFmScrobbleEnabled { get; set; }
    public string? LastFmApiKey { get; set; }
    public string? LastFmApiSecret { get; set; }
    public string? LastFmSessionKey { get; set; }
    public string? LastFmUsername { get; set; }
    public bool ScriptHooksEnabled { get; set; }
    public bool NotifyOnTrackChange { get; set; }
    public int SleepTimerMinutes { get; set; } = 30;
    public bool AutoDownloadLyrics { get; set; } = true;
    public bool EmbedLyricsInTags { get; set; }
    public bool MarkNewImportsAsInbox { get; set; } = true;
    public double LeftSidebarWidth { get; set; } = 240;
    public double RightSidebarWidth { get; set; } = 300;
    public bool LeftSidebarVisible { get; set; } = true;
    public bool RightSidebarVisible { get; set; } = true;
    /// <summary>When EqPreset is "custom", these 10 band gains (dB) are used.</summary>
    public string? AcoustIdApiKey { get; set; }
    public string? FpcalcPath { get; set; }
    public List<float> CustomEqBands { get; set; } = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    public bool AutoCheckUpdates { get; set; } = true;
    public string? DismissedUpdateVersion { get; set; }
    public string? LastUpdateCheckUtc { get; set; }
}
