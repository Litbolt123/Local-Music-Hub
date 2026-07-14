using System.Windows;
using LocalMusicHub.Services;

namespace LocalMusicHub;

public partial class MiniPlayerWindow
{
    private readonly PlaybackService _playback;
    private readonly Func<System.Windows.Media.ImageSource?> _getCover;

    public MiniPlayerWindow(PlaybackService playback, Func<System.Windows.Media.ImageSource?> getCover)
    {
        _playback = playback;
        _getCover = getCover;
        HubTheme.Ensure(this);
        InitializeComponent();
        _playback.TrackChanged += (_, _) => Dispatcher.Invoke(Refresh);
        _playback.StateChanged += (_, _) => Dispatcher.Invoke(Refresh);
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var track = _playback.CurrentTrack;
        if (track is null)
        {
            TitleText.Text = "Nothing playing";
            ArtistText.Text = "";
            SetPlayPauseIcons(playing: false);
            CoverImage.Source = null;
            return;
        }

        TitleText.Text = track.DisplayTitle;
        ArtistText.Text = track.DisplayArtist;
        SetPlayPauseIcons(_playback.IsPlaying);
        CoverImage.Source = _getCover();
    }

    private void SetPlayPauseIcons(bool playing)
    {
        if (PlayPausePlayIcon is not null)
            PlayPausePlayIcon.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        if (PlayPausePauseIcon is not null)
            PlayPausePauseIcon.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        if (PlayPauseButton is not null)
            PlayPauseButton.ToolTip = playing ? "Pause" : "Play";
    }

    private void PlayPause_OnClick(object sender, RoutedEventArgs e)
    {
        if (_playback.CurrentTrack is null)
            return;
        _playback.TogglePlayPause();
        Refresh();
    }

    private void Previous_OnClick(object sender, RoutedEventArgs e) => _playback.Previous();

    private void Next_OnClick(object sender, RoutedEventArgs e) => _playback.Next();

    private void Expand_OnClick(object sender, RoutedEventArgs e)
    {
        if (Owner is MainWindow main)
        {
            main.Show();
            main.WindowState = WindowState.Normal;
            main.Activate();
        }

        Close();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    public void CloseFromOwner() => Close();
}
