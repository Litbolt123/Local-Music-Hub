using System.IO;
using System.Windows;
using LocalMusicHub.Models;
using LocalMusicHub.Services;
using MessageBox = System.Windows.MessageBox;

namespace LocalMusicHub;

public partial class CoverAdjustWindow
{
    private readonly LibraryTrack _seedTrack;
    private readonly IReadOnlyList<LibraryTrack> _albumTracks;
    private byte[]? _sourceBytes;

    public byte[]? ResultCover { get; private set; }
    public bool ApplyToAlbum { get; private set; }

    public CoverAdjustWindow(LibraryTrack seedTrack, IReadOnlyList<LibraryTrack> albumTracks)
    {
        HubTheme.Ensure(this);
        InitializeComponent();
        _seedTrack = seedTrack;
        _albumTracks = albumTracks.Count > 0 ? albumTracks : [seedTrack];
        _sourceBytes = seedTrack.CoverArt;

        SubtitleText.Text = $"{seedTrack.DisplayAlbumArtist} — {seedTrack.DisplayAlbum}\n{seedTrack.DisplayTitle}";
        ApplyAlbumBox.IsChecked = true;
        ApplyAlbumBox.Content = $"Apply to all {_albumTracks.Count} track(s) on “{seedTrack.DisplayAlbum}”";
        RefreshPreview();
    }

    private void Offset_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        RefreshPreview();

    private void Center_OnClick(object sender, RoutedEventArgs e)
    {
        OffsetXSlider.Value = 0;
        OffsetYSlider.Value = 0;
        RefreshPreview();
    }

    private void ChooseImage_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose cover image",
            Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp|All files|*.*",
        };
        if (dlg.ShowDialog(this) != true)
            return;

        var bytes = CoverArtHelper.LoadImageFile(dlg.FileName);
        if (bytes is not { Length: > 0 })
        {
            MessageBox.Show(this, "Could not read that image.", "Adjust cover art",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _sourceBytes = bytes;
        OffsetXSlider.Value = 0;
        OffsetYSlider.Value = 0;
        StatusText.Text = $"Loaded {Path.GetFileName(dlg.FileName)}";
        RefreshPreview();
    }

    private void MusicBrainz_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new MusicBrainzAlbumCoverWindow(_seedTrack.DisplayAlbumArtist, _seedTrack.DisplayAlbum)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true || dlg.ResultCover is not { Length: > 0 } cover)
            return;

        _sourceBytes = cover;
        OffsetXSlider.Value = 0;
        OffsetYSlider.Value = 0;
        StatusText.Text = "Loaded cover from Apple Music / Deezer — adjust crop, then Save.";
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        PreviewImage.Source = CoverArtHelper.ToBitmap(
            _sourceBytes,
            decodePixelWidth: 240,
            offsetX: OffsetXSlider.Value,
            offsetY: OffsetYSlider.Value,
            centerCropSquare: true);
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (_sourceBytes is not { Length: > 0 })
        {
            MessageBox.Show(this, "No cover image to save.", "Adjust cover art",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var encoded = CoverArtHelper.EncodeJpegSquare(
            _sourceBytes,
            OffsetXSlider.Value,
            OffsetYSlider.Value);
        if (encoded is not { Length: > 0 })
        {
            MessageBox.Show(this, "Could not encode the cover image.", "Adjust cover art",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultCover = encoded;
        ApplyToAlbum = ApplyAlbumBox.IsChecked == true;
        DialogResult = true;
        Close();
    }
}
