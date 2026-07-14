using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LocalMusicHub.Models;
using LocalMusicHub.Services;

namespace LocalMusicHub;

public partial class LyricsWindow
{
    private LibraryTrack _track;
    private readonly Func<TimeSpan> _getPosition;
    private CancellationTokenSource? _cts;
    private IReadOnlyList<LyricsLine> _timedLines = [];
    private readonly DispatcherTimer _syncTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private int _lastIndex = -1;

    public LyricsWindow(LibraryTrack track, Func<TimeSpan> getPosition)
    {
        _track = track;
        _getPosition = getPosition;
        HubTheme.Ensure(this);
        InitializeComponent();
        TrackTitleText.Text = $"{track.DisplayTitle} — {track.DisplayArtist}";
        SourceText.Text = "Loading lyrics…";
        LyricsText.Text = "";
        _syncTimer.Tick += (_, _) => SyncHighlight();
        Loaded += async (_, _) => await LoadAsync();
        Closed += (_, _) => _syncTimer.Stop();
    }

    public void ShowTrack(LibraryTrack track)
    {
        _track = track;
        TrackTitleText.Text = $"{track.DisplayTitle} — {track.DisplayArtist}";
        _ = LoadAsync();
    }

    private async void Refresh_OnClick(object sender, RoutedEventArgs e) =>
        await LoadAsync();

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private async Task LoadAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SourceText.Text = "Loading lyrics…";
        LyricsText.Text = "";
        SyncedList.ItemsSource = null;
        PlainScroll.Visibility = Visibility.Visible;
        SyncedList.Visibility = Visibility.Collapsed;
        _syncTimer.Stop();
        _timedLines = [];
        _lastIndex = -1;

        try
        {
            var result = await LyricsService.GetLyricsAsync(_track, token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
                return;

            if (result.Found)
            {
                SourceText.Text = $"Source: {result.Source}";
                if (result.IsSynced)
                {
                    _timedLines = result.TimedLines;
                    SyncedList.ItemsSource = _timedLines.Select(l => l.Text).ToList();
                    PlainScroll.Visibility = Visibility.Collapsed;
                    SyncedList.Visibility = Visibility.Visible;
                    _syncTimer.Start();
                    SyncHighlight();
                }
                else
                {
                    LyricsText.Text = result.Text;
                }
            }
            else
            {
                SourceText.Text = "No lyrics";
                LyricsText.Text = result.Text +
                    "\n\nTip: lyrics are fetched from LRCLIB and cached next to the file (or in app cache). " +
                    "You can also place a .txt or .lrc sidecar yourself.";
            }
        }
        catch (OperationCanceledException)
        {
            /* ignored */
        }
        catch (Exception ex)
        {
            SourceText.Text = "Error";
            LyricsText.Text = ex.Message;
        }
    }

    private void SyncHighlight()
    {
        if (_timedLines.Count == 0)
            return;

        var pos = _getPosition();
        var index = 0;
        for (var i = 0; i < _timedLines.Count; i++)
        {
            if (_timedLines[i].Time <= pos)
                index = i;
            else
                break;
        }

        if (index == _lastIndex)
            return;

        _lastIndex = index;
        SyncedList.SelectedIndex = index;
        if (SyncedList.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem item)
            item.BringIntoView();
        else
            SyncedList.ScrollIntoView(SyncedList.SelectedItem);
    }
}
