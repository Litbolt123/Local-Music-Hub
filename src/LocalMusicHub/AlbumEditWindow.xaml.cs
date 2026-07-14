using System.Windows;
using LocalMusicHub.Services;
using MessageBox = System.Windows.MessageBox;

namespace LocalMusicHub;

public partial class AlbumEditWindow
{
    public string? AlbumArtist { get; private set; }
    public string? Album { get; private set; }

    public AlbumEditWindow(string albumArtist, string album, int trackCount)
    {
        HubTheme.Ensure(this);
        InitializeComponent();
        AlbumArtistBox.Text = albumArtist;
        AlbumBox.Text = album;
        SubtitleText.Text = $"Updates album artist and album name on all {trackCount} track(s).";
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        var album = AlbumBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(album))
        {
            MessageBox.Show(this, "Album name is required.", "Edit album",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AlbumArtist = AlbumArtistBox.Text.Trim();
        Album = album;
        DialogResult = true;
        Close();
    }
}
