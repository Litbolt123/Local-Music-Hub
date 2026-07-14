using System.Windows;
using System.Windows.Controls;
using LocalMusicHub.Services;
using MessageBox = System.Windows.MessageBox;

namespace LocalMusicHub;

public partial class MusicBrainzAlbumCoverWindow
{
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _coverCts;
    private byte[]? _previewCoverBytes;
    private string? _previewCoverKey;
    private bool _coverDownloadInFlight;

    public byte[]? ResultCover { get; private set; }

    public MusicBrainzAlbumCoverWindow(string albumArtist, string album)
    {
        HubTheme.Ensure(this);
        InitializeComponent();
        ArtistBox.Text = albumArtist;
        AlbumBox.Text = album;
        AlbumText.Text =
            $"Covers are fetched from Apple Music / Deezer. Apply to every track in “{album}”.";
        StatusText.Text = "Search MusicBrainz for release names, then fetch cover from Apple/Deezer.";
        Loaded += async (_, _) => await SearchAsync();
    }

    private async void Search_OnClick(object sender, RoutedEventArgs e) =>
        await SearchAsync();

    private async Task SearchAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        StatusText.Text = "Searching MusicBrainz releases…";
        ResultsList.ItemsSource = null;
        ClearPreview();

        try
        {
            var results = await MusicBrainzService.SearchReleasesAsync(
                ArtistBox.Text.Trim(), AlbumBox.Text.Trim(), token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
                return;

            ResultsList.ItemsSource = results;
            StatusText.Text = results.Count == 0
                ? "No releases found — cover lookup will still use the artist/album boxes above."
                : $"{results.Count} release{(results.Count == 1 ? "" : "s")} — select one (or Apply uses the boxes).";
            if (results.Count > 0)
                ResultsList.SelectedIndex = 0;
            else
                _ = PreviewForAsync(ArtistBox.Text.Trim(), AlbumBox.Text.Trim());
        }
        catch (OperationCanceledException) { /* ignored */ }
        catch (Exception ex)
        {
            StatusText.Text = $"Search failed: {ex.Message}";
        }
    }

    private void ResultsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is MusicBrainzReleaseHit hit)
        {
            var artist = string.IsNullOrWhiteSpace(hit.Artist) ? ArtistBox.Text.Trim() : hit.Artist;
            var album = string.IsNullOrWhiteSpace(hit.Title) ? AlbumBox.Text.Trim() : hit.Title;
            _ = PreviewForAsync(artist, album);
            return;
        }

        ClearPreview();
    }

    private async Task PreviewForAsync(string artist, string album)
    {
        if (artist.Length == 0 && album.Length == 0)
        {
            ClearPreview();
            return;
        }

        var key = $"{artist}\n{album}".ToLowerInvariant();
        if (string.Equals(_previewCoverKey, key, StringComparison.Ordinal) &&
            _previewCoverBytes is { Length: > 0 })
        {
            CoverPreview.Source = CoverArtHelper.ToBitmap(_previewCoverBytes, 150, centerCropSquare: true);
            CoverProgress.Visibility = Visibility.Collapsed;
            CoverStatusText.Text = "Cover ready.";
            return;
        }

        _coverCts?.Cancel();
        _coverCts = new CancellationTokenSource();
        var token = _coverCts.Token;
        _coverDownloadInFlight = true;
        CoverPreview.Source = null;
        CoverProgress.Visibility = Visibility.Visible;
        CoverProgress.IsIndeterminate = true;
        CoverStatusText.Text = "Looking up cover (Apple / Deezer)… Apply is OK.";

        var progress = new Progress<CoverArtProgress>(p =>
        {
            CoverProgress.Visibility = Visibility.Visible;
            CoverProgress.IsIndeterminate = p.Total <= 0;
            if (p.Total > 0)
                CoverProgress.Value = p.Fraction;
            CoverStatusText.Text = p.Message + " — Apply is OK.";
            StatusText.Text = p.Message;
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
            CoverPreview.Source = CoverArtHelper.ToBitmap(result.Bytes, 150, centerCropSquare: true);
            CoverStatusText.Text = "Cover ready — Apply to all tracks in the album.";
        }
        catch (OperationCanceledException) { /* ignored */ }
        catch (Exception ex)
        {
            _coverDownloadInFlight = false;
            CoverProgress.IsIndeterminate = false;
            CoverStatusText.Text = $"Cover fetch failed: {ex.Message}";
        }
    }

    private void ClearPreview()
    {
        _previewCoverBytes = null;
        _previewCoverKey = null;
        _coverDownloadInFlight = false;
        CoverPreview.Source = null;
        CoverProgress.Visibility = Visibility.Collapsed;
        CoverStatusText.Text = "Select a release.";
    }

    private async void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        string artist;
        string album;
        if (ResultsList.SelectedItem is MusicBrainzReleaseHit hit)
        {
            artist = string.IsNullOrWhiteSpace(hit.Artist) ? ArtistBox.Text.Trim() : hit.Artist;
            album = string.IsNullOrWhiteSpace(hit.Title) ? AlbumBox.Text.Trim() : hit.Title;
        }
        else
        {
            artist = ArtistBox.Text.Trim();
            album = AlbumBox.Text.Trim();
        }

        var key = $"{artist}\n{album}".ToLowerInvariant();
        if (_previewCoverBytes is { Length: > 0 } &&
            string.Equals(_previewCoverKey, key, StringComparison.Ordinal))
        {
            ResultCover = _previewCoverBytes;
            DialogResult = true;
            Close();
            return;
        }

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
            var result = await MusicBrainzService.FetchAlbumCoverAsync(artist, album, progress)
                .ConfigureAwait(true);
            if (!result.Succeeded || result.Bytes is not { Length: > 0 })
            {
                MessageBox.Show(this, result.Error ?? "Could not download cover art.", "MusicBrainz",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResultCover = result.Bytes;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not download cover:\n{ex.Message}", "MusicBrainz",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
