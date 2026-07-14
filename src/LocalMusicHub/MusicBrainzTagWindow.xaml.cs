using System.Windows;
using System.Windows.Controls;
using LocalMusicHub.Models;
using LocalMusicHub.Services;
using MessageBox = System.Windows.MessageBox;

namespace LocalMusicHub;

public partial class MusicBrainzTagWindow
{
    private readonly LibraryTrack _track;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _coverCts;
    private byte[]? _previewCoverBytes;
    private string? _previewCoverKey;
    private bool _coverDownloadInFlight;
    private bool _suppressReleasePreview;

    public LibraryTrack? Result { get; private set; }

    public MusicBrainzTagWindow(LibraryTrack track)
    {
        _track = track;
        HubTheme.Ensure(this);
        InitializeComponent();
        CurrentText.Text = $"Current: {track.DisplayArtist} — {track.DisplayTitle}";
        ArtistBox.Text = track.DisplayArtist;
        TitleBox.Text = track.DisplayTitle;
        StatusText.Text = "Search MusicBrainz for tags. Covers come from Apple Music / Deezer.";
        ShowCurrentCover();
        Loaded += async (_, _) => await SearchAsync();
    }

    private void ShowCurrentCover()
    {
        CoverProgress.Visibility = Visibility.Collapsed;
        CoverPreview.Source = CoverArtHelper.ToBitmap(_track.CoverArt, 140, centerCropSquare: true);
        CoverStatusText.Text = _track.CoverArt is { Length: > 0 }
            ? "Current cover (until a new one is fetched)."
            : "No current cover.";
    }

    private async void Search_OnClick(object sender, RoutedEventArgs e) =>
        await SearchAsync();

    private async Task SearchAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        StatusText.Text = "Searching MusicBrainz…";
        ResultsList.ItemsSource = null;
        _suppressReleasePreview = true;
        ReleaseCombo.ItemsSource = null;
        _suppressReleasePreview = false;
        ClearPreviewCache();
        ShowCurrentCover();

        try
        {
            var results = await MusicBrainzService.SearchRecordingsAsync(
                ArtistBox.Text.Trim(), TitleBox.Text.Trim(), token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
                return;

            ResultsList.ItemsSource = results;
            StatusText.Text = results.Count == 0
                ? "No recordings found. Try different artist/title text."
                : $"{results.Count} result{(results.Count == 1 ? "" : "s")} — select one; Apply works while cover downloads.";
            if (results.Count > 0)
                ResultsList.SelectedIndex = 0;
        }
        catch (OperationCanceledException) { /* ignored */ }
        catch (Exception ex)
        {
            StatusText.Text = $"Search failed: {ex.Message}";
        }
    }

    private void ResultsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is not MusicBrainzRecording match)
        {
            _suppressReleasePreview = true;
            ReleaseCombo.ItemsSource = null;
            _suppressReleasePreview = false;
            ClearPreviewCache();
            ShowCurrentCover();
            return;
        }

        var options = new List<ReleaseOption>
        {
            new(match.Artist, match.Album, match.ReleaseId, match.Year, match.TrackNumber, isPrimary: true),
        };
        foreach (var alt in match.AlternateReleases)
            options.Add(new ReleaseOption(match.Artist, alt.Album, alt.ReleaseId, alt.Year, alt.TrackNumber, isPrimary: false));

        _suppressReleasePreview = true;
        ReleaseCombo.ItemsSource = options;
        ReleaseCombo.SelectedIndex = 0;
        _suppressReleasePreview = false;
        _ = PreviewSelectedReleaseAsync(auto: true);
    }

    private void ReleaseCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressReleasePreview)
            return;
        _ = PreviewSelectedReleaseAsync(auto: true);
    }

    private void FetchCoverBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        if (FetchCoverBox.IsChecked != true)
        {
            ClearPreviewCache();
            ShowCurrentCover();
            CoverStatusText.Text = "Cover will not be applied.";
            return;
        }

        _ = PreviewSelectedReleaseAsync(auto: true);
    }

    private async void PreviewCover_OnClick(object sender, RoutedEventArgs e) =>
        await PreviewSelectedReleaseAsync(auto: false);

    private (string Artist, string Album, string Key)? ResolveCoverQuery()
    {
        if (ReleaseCombo.SelectedItem is ReleaseOption release)
        {
            var artist = string.IsNullOrWhiteSpace(release.Artist) ? ArtistBox.Text.Trim() : release.Artist;
            var album = string.IsNullOrWhiteSpace(release.Album) ? _track.DisplayAlbum : release.Album;
            if (artist.Length == 0 && album.Length == 0)
                return null;
            return (artist, album, $"{artist}\n{album}".ToLowerInvariant());
        }

        if (ResultsList.SelectedItem is MusicBrainzRecording match)
        {
            var artist = string.IsNullOrWhiteSpace(match.Artist) ? ArtistBox.Text.Trim() : match.Artist;
            var album = string.IsNullOrWhiteSpace(match.Album) ? _track.DisplayAlbum : match.Album;
            return (artist, album, $"{artist}\n{album}".ToLowerInvariant());
        }

        return null;
    }

    private async Task PreviewSelectedReleaseAsync(bool auto)
    {
        if (FetchCoverBox.IsChecked != true && auto)
            return;

        var query = ResolveCoverQuery();
        if (query is null)
        {
            ClearPreviewCache();
            CoverPreview.Source = null;
            CoverProgress.Visibility = Visibility.Collapsed;
            CoverStatusText.Text = "Need artist/album to look up a cover.";
            return;
        }

        var (artist, album, key) = query.Value;
        if (string.Equals(_previewCoverKey, key, StringComparison.Ordinal) &&
            _previewCoverBytes is { Length: > 0 })
        {
            CoverPreview.Source = CoverArtHelper.ToBitmap(_previewCoverBytes, 140, centerCropSquare: true);
            CoverProgress.Visibility = Visibility.Collapsed;
            CoverStatusText.Text = "Cover ready — will be applied.";
            return;
        }

        _coverCts?.Cancel();
        _coverCts = new CancellationTokenSource();
        var token = _coverCts.Token;
        _coverDownloadInFlight = true;
        CoverPreview.Source = null;
        CoverProgress.Visibility = Visibility.Visible;
        CoverProgress.IsIndeterminate = true;
        CoverProgress.Value = 0;
        CoverStatusText.Text = "Looking up cover (Apple / Deezer)… Apply is OK.";

        var progress = new Progress<CoverArtProgress>(p =>
        {
            CoverProgress.Visibility = Visibility.Visible;
            CoverProgress.IsIndeterminate = p.Total <= 0;
            if (p.Total > 0)
            {
                CoverProgress.Maximum = 1;
                CoverProgress.Value = p.Fraction;
            }

            CoverStatusText.Text = p.Message + " — Apply is OK.";
        });

        try
        {
            var result = await MusicBrainzService.FetchAlbumCoverAsync(artist, album, progress, token)
                .ConfigureAwait(true);
            if (token.IsCancellationRequested)
                return;

            _coverDownloadInFlight = false;
            CoverProgress.IsIndeterminate = false;
            if (!result.Succeeded)
            {
                CoverProgress.Value = 0;
                CoverStatusText.Text = result.Error ?? "No cover found.";
                return;
            }

            _previewCoverBytes = result.Bytes;
            _previewCoverKey = key;
            CoverProgress.Value = 1;
            CoverPreview.Source = CoverArtHelper.ToBitmap(result.Bytes, 140, centerCropSquare: true);
            CoverStatusText.Text = "Cover ready — will be applied.";
        }
        catch (OperationCanceledException) { /* ignored */ }
        catch (Exception ex)
        {
            _coverDownloadInFlight = false;
            CoverProgress.IsIndeterminate = false;
            CoverStatusText.Text = $"Cover fetch failed: {ex.Message}";
        }
    }

    private void ClearPreviewCache()
    {
        _previewCoverBytes = null;
        _previewCoverKey = null;
        _coverDownloadInFlight = false;
    }

    private async void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not MusicBrainzRecording match)
        {
            MessageBox.Show(this, "Select a MusicBrainz result first.", "MusicBrainz",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selected = match;
        if (ReleaseCombo.SelectedItem is ReleaseOption release)
        {
            selected = match.WithRelease(new MusicBrainzReleaseInfo(
                release.Album, release.ReleaseId, release.Year, release.TrackNumber));
        }

        byte[]? cover = null;
        if (FetchCoverBox.IsChecked == true)
        {
            var query = ResolveCoverQuery();
            if (query is { } q)
            {
                if (_previewCoverBytes is { Length: > 0 } &&
                    string.Equals(_previewCoverKey, q.Key, StringComparison.Ordinal))
                {
                    cover = _previewCoverBytes;
                }
                else
                {
                    StatusText.Text = _coverDownloadInFlight
                        ? "Waiting for cover download…"
                        : "Downloading cover (Apple / Deezer)…";
                    CoverProgress.Visibility = Visibility.Visible;
                    CoverProgress.IsIndeterminate = true;
                    try
                    {
                        var progress = new Progress<CoverArtProgress>(p =>
                        {
                            CoverProgress.IsIndeterminate = p.Total <= 0;
                            if (p.Total > 0)
                                CoverProgress.Value = p.Fraction;
                            CoverStatusText.Text = p.Message;
                            StatusText.Text = p.Message;
                        });
                        var result = await MusicBrainzService.FetchAlbumCoverAsync(
                            q.Artist, q.Album, progress).ConfigureAwait(true);
                        cover = result.Bytes;
                        if (!result.Succeeded)
                            StatusText.Text = (result.Error ?? "No cover found") + " — applying tags only.";
                    }
                    catch (Exception ex)
                    {
                        StatusText.Text = $"Cover failed ({ex.Message}) — applying tags only.";
                    }
                }
            }
        }

        Result = MusicBrainzService.ApplyToTrack(_track, selected, cover);
        DialogResult = true;
        Close();
    }

    private sealed record ReleaseOption(
        string Artist,
        string Album,
        string? ReleaseId,
        int? Year,
        int? TrackNumber,
        bool isPrimary)
    {
        public string Label
        {
            get
            {
                var name = string.IsNullOrWhiteSpace(Album) ? "(untitled release)" : Album;
                var year = Year is > 0 ? $" ({Year})" : "";
                var primary = isPrimary ? "" : " · alternate";
                return $"{name}{year}{primary}";
            }
        }
    }
}
