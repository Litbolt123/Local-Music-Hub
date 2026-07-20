using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LocalMusicHub.Services;

namespace LocalMusicHub;

public partial class MiniPlayerWindow
{
    private readonly PlaybackService _playback;
    private readonly Func<System.Windows.Media.ImageSource?> _getCover;
    private readonly DispatcherTimer _positionTimer;
    private bool _suppressVolumeSlider;
    private bool _positionScrubbing;

    public MiniPlayerWindow(PlaybackService playback, Func<System.Windows.Media.ImageSource?> getCover)
    {
        _playback = playback;
        _getCover = getCover;
        HubTheme.Ensure(this);
        InitializeComponent();

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _positionTimer.Tick += (_, _) => UpdatePositionUi();

        _playback.TrackChanged += (_, _) => Dispatcher.Invoke(Refresh);
        _playback.StateChanged += (_, _) => Dispatcher.Invoke(Refresh);
        HubTheme.ThemeChanged += OnThemeChanged;
        Loaded += (_, _) => Refresh();
        Closed += (_, _) => HubTheme.ThemeChanged -= OnThemeChanged;
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
            UpdatePositionUi();
            _positionTimer.Stop();
            return;
        }

        TitleText.Text = track.DisplayTitle;
        ArtistText.Text = track.DisplayArtist;
        SetPlayPauseIcons(_playback.IsPlaying);
        CoverImage.Source = _getCover();

        if (!_suppressVolumeSlider)
        {
            _suppressVolumeSlider = true;
            VolumeSlider.Value = _playback.Volume;
            _suppressVolumeSlider = false;
        }

        UpdatePositionUi();
        if (_playback.IsPlaying && !_playback.IsPaused)
            _positionTimer.Start();
        else
            _positionTimer.Stop();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Refresh();

    private void SetPlayPauseIcons(bool playing)
    {
        PlayPausePlayIcon.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        PlayPausePauseIcon.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        PlayPauseButton.ToolTip = playing ? "Pause" : "Play";
    }

    private void UpdatePositionUi()
    {
        if (_positionScrubbing)
            return;

        var duration = _playback.Duration;
        var position = _playback.Position;
        if (duration.TotalSeconds <= 0)
        {
            ElapsedText.Text = "0:00";
            RemainingText.Text = "0:00";
            PositionSlider.Value = 0;
            return;
        }

        ElapsedText.Text = FormatTime(position);
        RemainingText.Text = duration > position
            ? $"-{FormatTime(duration - position)}"
            : "0:00";

        var fraction = Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1);
        PositionSlider.Value = fraction;
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private void PlayPause_OnClick(object sender, RoutedEventArgs e)
    {
        if (_playback.CurrentTrack is null)
            return;
        _playback.TogglePlayPause();
        Refresh();
    }

    private void Previous_OnClick(object sender, RoutedEventArgs e) => _playback.Previous();

    private void Next_OnClick(object sender, RoutedEventArgs e) => _playback.Next();

    private void VolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressVolumeSlider)
            return;
        _playback.SetVolume(VolumeSlider.Value);
    }

    private void PositionSlider_OnPreviewMouseDown(object sender, MouseButtonEventArgs e) => _positionScrubbing = true;

    private void PositionSlider_OnPreviewMouseUp(object sender, MouseButtonEventArgs e) => CommitPositionSeek();

    private void PositionSlider_OnLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e) => CommitPositionSeek();

    private void CommitPositionSeek()
    {
        if (!_positionScrubbing)
            return;

        _positionScrubbing = false;
        var duration = _playback.Duration;
        if (duration.TotalSeconds <= 0)
            return;

        var target = TimeSpan.FromSeconds(PositionSlider.Value * duration.TotalSeconds);
        _playback.Seek(target);
        UpdatePositionUi();
    }

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
