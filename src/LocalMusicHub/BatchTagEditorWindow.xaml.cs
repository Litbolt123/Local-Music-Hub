using System.Windows;
using LocalMusicHub.Services;
using MessageBox = System.Windows.MessageBox;

namespace LocalMusicHub;

public partial class BatchTagEditorWindow
{
    private int _rating;

    public BatchTagPatch? Patch { get; private set; }

    public BatchTagEditorWindow(int trackCount)
    {
        HubTheme.Ensure(this);
        InitializeComponent();
        SummaryText.Text = $"Check the fields you want to overwrite on {trackCount} track{(trackCount == 1 ? "" : "s")}.";
        RatingText.Text = "—";
    }

    private void Rating_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !int.TryParse(tag, out var rating))
            return;

        _rating = Math.Clamp(rating, 0, 5);
        ApplyRatingBox.IsChecked = true;
        RatingText.Text = _rating <= 0 ? "—" : new string('★', _rating);
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (ApplyTitleBox.IsChecked != true &&
            ApplyArtistBox.IsChecked != true &&
            ApplyAlbumArtistBox.IsChecked != true &&
            ApplyAlbumBox.IsChecked != true &&
            ApplyTrackNumberBox.IsChecked != true &&
            ApplyYearBox.IsChecked != true &&
            ApplyGenreBox.IsChecked != true &&
            ApplyRatingBox.IsChecked != true)
        {
            MessageBox.Show(this, "Check at least one field to apply.", "Edit tags (batch)",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int? trackNumber = null;
        if (ApplyTrackNumberBox.IsChecked == true)
        {
            if (!string.IsNullOrWhiteSpace(TrackNumberBox.Text))
            {
                if (!int.TryParse(TrackNumberBox.Text.Trim(), out var tn) || tn < 0)
                {
                    MessageBox.Show(this, "Track number must be a non-negative integer.", "Edit tags (batch)",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                trackNumber = tn == 0 ? null : tn;
            }
        }

        int? year = null;
        if (ApplyYearBox.IsChecked == true)
        {
            if (!string.IsNullOrWhiteSpace(YearBox.Text))
            {
                if (!int.TryParse(YearBox.Text.Trim(), out var y) || y < 0)
                {
                    MessageBox.Show(this, "Year must be a non-negative integer.", "Edit tags (batch)",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                year = y == 0 ? null : y;
            }
        }

        Patch = new BatchTagPatch
        {
            ApplyTitle = ApplyTitleBox.IsChecked == true,
            Title = TitleBox.Text,
            ApplyArtist = ApplyArtistBox.IsChecked == true,
            Artist = ArtistBox.Text,
            ApplyAlbumArtist = ApplyAlbumArtistBox.IsChecked == true,
            AlbumArtist = AlbumArtistBox.Text,
            ApplyAlbum = ApplyAlbumBox.IsChecked == true,
            Album = AlbumBox.Text,
            ApplyTrackNumber = ApplyTrackNumberBox.IsChecked == true,
            TrackNumber = trackNumber,
            ApplyYear = ApplyYearBox.IsChecked == true,
            Year = year,
            ApplyGenre = ApplyGenreBox.IsChecked == true,
            Genre = GenreBox.Text,
            ApplyRating = ApplyRatingBox.IsChecked == true,
            Rating = _rating,
        };

        DialogResult = true;
        Close();
    }
}
