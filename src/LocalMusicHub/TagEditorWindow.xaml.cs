using System.Windows;
using LocalMusicHub.Models;
using LocalMusicHub.Services;
using MessageBox = System.Windows.MessageBox;

namespace LocalMusicHub;

public partial class TagEditorWindow
{
    private readonly LibraryTrack _original;
    private int _rating;

    public LibraryTrack? Result { get; private set; }

    public TagEditorWindow(LibraryTrack track)
    {
        HubTheme.Ensure(this);
        InitializeComponent();
        _original = track;
        _rating = track.Rating;

        FilePathText.Text = track.FilePath;
        TitleBox.Text = track.Title;
        ArtistBox.Text = track.Artist;
        AlbumArtistBox.Text = track.AlbumArtist;
        AlbumBox.Text = track.Album;
        TrackNumberBox.Text = track.TrackNumber?.ToString() ?? "";
        YearBox.Text = track.Year?.ToString() ?? "";
        GenreBox.Text = track.Genre;
        RatingText.Text = track.RatingLabel;
    }

    private void Rating_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !int.TryParse(tag, out var rating))
            return;
        _rating = Math.Clamp(rating, 0, 5);
        RatingText.Text = _rating <= 0 ? "—" : new string('★', _rating);
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        int? trackNumber = null;
        if (!string.IsNullOrWhiteSpace(TrackNumberBox.Text))
        {
            if (!int.TryParse(TrackNumberBox.Text.Trim(), out var tn) || tn < 0)
            {
                MessageBox.Show(this, "Track number must be a non-negative integer.", "Edit tags",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            trackNumber = tn == 0 ? null : tn;
        }

        int? year = null;
        if (!string.IsNullOrWhiteSpace(YearBox.Text))
        {
            if (!int.TryParse(YearBox.Text.Trim(), out var y) || y < 0)
            {
                MessageBox.Show(this, "Year must be a non-negative integer.", "Edit tags",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            year = y == 0 ? null : y;
        }

        Result = new LibraryTrack
        {
            Id = _original.Id,
            FilePath = _original.FilePath,
            Title = TitleBox.Text.Trim(),
            Artist = ArtistBox.Text.Trim(),
            AlbumArtist = AlbumArtistBox.Text.Trim(),
            Album = AlbumBox.Text.Trim(),
            TrackNumber = trackNumber,
            Year = year,
            Genre = GenreBox.Text.Trim(),
            Duration = _original.Duration,
            Bitrate = _original.Bitrate,
            Format = _original.Format,
            DateAddedUtc = _original.DateAddedUtc,
            FileModifiedUtc = DateTime.UtcNow,
            CoverArt = _original.CoverArt,
            PlayCount = _original.PlayCount,
            LastPlayedUtc = _original.LastPlayedUtc,
            Rating = _rating,
        };

        DialogResult = true;
        Close();
    }
}
