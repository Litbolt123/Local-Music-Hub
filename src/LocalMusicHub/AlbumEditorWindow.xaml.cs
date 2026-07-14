using System.IO;
using System.Windows;
using LocalMusicHub.Models;
using LocalMusicHub.Services;
using MessageBox = System.Windows.MessageBox;

namespace LocalMusicHub;

public partial class AlbumEditorWindow
{
    private readonly LibraryTrack _seedTrack;
    private readonly IReadOnlyList<LibraryTrack> _albumTracks;
    private readonly byte[]? _originalCover;
    private byte[]? _sourceBytes;
    private bool _removeCover;

    public string? ResultAlbumArtist { get; private set; }
    public string? ResultAlbum { get; private set; }
    public byte[]? ResultCover { get; private set; }
    public bool ClearCover { get; private set; }
    public bool UpdateCover { get; private set; }

    public AlbumEditorWindow(
        LibraryTrack seedTrack,
        IReadOnlyList<LibraryTrack> albumTracks,
        string albumArtist,
        string album)
    {
        HubTheme.Ensure(this);
        InitializeComponent();
        _seedTrack = seedTrack;
        _albumTracks = albumTracks.Count > 0 ? albumTracks : [seedTrack];
        _originalCover = seedTrack.CoverArt;
        _sourceBytes = seedTrack.CoverArt;

        AlbumArtistBox.Text = albumArtist;
        AlbumBox.Text = album;
        SubtitleText.Text = $"{albumArtist} — {album}";
        TrackCountText.Text =
            $"Metadata and cover (optional) apply to all {_albumTracks.Count} track(s) on this album.";
        RefreshPreview();
    }

    private void Preview_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        RefreshPreview();

    private void Quality_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (QualityLabel is not null)
            QualityLabel.Text = ((int)QualitySlider.Value).ToString();
    }

    private void Center_OnClick(object sender, RoutedEventArgs e)
    {
        OffsetXSlider.Value = 0;
        OffsetYSlider.Value = 0;
        ZoomSlider.Value = 1;
        RefreshPreview();
    }

    private void ChooseImage_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose cover image",
            Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif|All files|*.*",
        };
        if (dlg.ShowDialog(this) != true)
            return;

        LoadCoverBytes(CoverArtHelper.LoadImageFile(dlg.FileName), $"Loaded {Path.GetFileName(dlg.FileName)}");
    }

    private void Paste_OnClick(object sender, RoutedEventArgs e)
    {
        var bytes = CoverArtHelper.TryGetClipboardImage();
        if (bytes is not { Length: > 0 })
        {
            MessageBox.Show(this, "Clipboard does not contain an image.", "Edit album",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LoadCoverBytes(bytes, "Pasted cover from clipboard");
    }

    private void FetchOnline_OnClick(object sender, RoutedEventArgs e)
    {
        var artist = string.IsNullOrWhiteSpace(AlbumArtistBox.Text)
            ? _seedTrack.DisplayAlbumArtist
            : AlbumArtistBox.Text.Trim();
        var album = string.IsNullOrWhiteSpace(AlbumBox.Text)
            ? _seedTrack.DisplayAlbum
            : AlbumBox.Text.Trim();

        var dlg = new MusicBrainzAlbumCoverWindow(artist, album) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultCover is not { Length: > 0 } cover)
            return;

        LoadCoverBytes(cover, "Loaded cover from Apple / Deezer");
    }

    private void ResetCover_OnClick(object sender, RoutedEventArgs e)
    {
        _removeCover = false;
        _sourceBytes = _originalCover;
        OffsetXSlider.Value = 0;
        OffsetYSlider.Value = 0;
        ZoomSlider.Value = 1;
        UpdateCoverBox.IsChecked = true;
        StatusText.Text = "Restored original cover";
        CoverStatusText.Text = _originalCover is { Length: > 0 }
            ? "Original cover restored."
            : "No original cover.";
        RefreshPreview();
    }

    private void RemoveCover_OnClick(object sender, RoutedEventArgs e)
    {
        _removeCover = true;
        _sourceBytes = null;
        UpdateCoverBox.IsChecked = true;
        PreviewImage.Source = null;
        StatusText.Text = "Cover will be removed on Save";
        CoverStatusText.Text = "Cover marked for removal.";
    }

    private void RotateLeft_OnClick(object sender, RoutedEventArgs e) => Rotate(-90);
    private void RotateRight_OnClick(object sender, RoutedEventArgs e) => Rotate(90);

    private void Rotate(int degrees)
    {
        if (_sourceBytes is not { Length: > 0 })
            return;

        var rotated = CoverArtHelper.RotateJpegOrPng(_sourceBytes, degrees);
        if (rotated is null)
        {
            MessageBox.Show(this, "Could not rotate that image.", "Edit album",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _removeCover = false;
        _sourceBytes = rotated;
        UpdateCoverBox.IsChecked = true;
        StatusText.Text = $"Rotated {degrees}°";
        RefreshPreview();
    }

    private void LoadCoverBytes(byte[]? bytes, string status)
    {
        if (bytes is not { Length: > 0 })
        {
            MessageBox.Show(this, "Could not read that image.", "Edit album",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _removeCover = false;
        _sourceBytes = bytes;
        OffsetXSlider.Value = 0;
        OffsetYSlider.Value = 0;
        ZoomSlider.Value = 1;
        UpdateCoverBox.IsChecked = true;
        StatusText.Text = status;
        CoverStatusText.Text = status;
        RefreshPreview();
    }

    private void Window_OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            e.Effects = System.Windows.DragDropEffects.Copy;
        else
            e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        var path = files[0];
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".gif"))
        {
            MessageBox.Show(this, "Drop a JPG, PNG, WEBP, BMP, or GIF image.", "Edit album",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LoadCoverBytes(CoverArtHelper.LoadImageFile(path), $"Loaded {Path.GetFileName(path)}");
    }

    private void RefreshPreview()
    {
        if (!IsLoaded)
            return;

        PreviewImage.Source = CoverArtHelper.ToBitmap(
            _sourceBytes,
            decodePixelWidth: 220,
            offsetX: OffsetXSlider.Value,
            offsetY: OffsetYSlider.Value,
            centerCropSquare: true,
            zoom: ZoomSlider.Value);
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        var album = AlbumBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(album))
        {
            MessageBox.Show(this, "Album name is required.", "Edit album",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ResultAlbumArtist = AlbumArtistBox.Text.Trim();
        ResultAlbum = album;
        UpdateCover = UpdateCoverBox.IsChecked == true;
        ClearCover = UpdateCover && _removeCover;

        if (UpdateCover && !_removeCover)
        {
            if (_sourceBytes is not { Length: > 0 })
            {
                // Leave cover unchanged if checkbox on but no image and not removing.
                UpdateCover = false;
            }
            else
            {
                var size = 800;
                if (OutputSizeBox.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: string tag } &&
                    int.TryParse(tag, out var parsed))
                    size = parsed;

                var encoded = CoverArtHelper.EncodeJpegSquare(
                    _sourceBytes,
                    OffsetXSlider.Value,
                    OffsetYSlider.Value,
                    outputSize: size,
                    quality: (int)QualitySlider.Value,
                    zoom: ZoomSlider.Value);
                if (encoded is not { Length: > 0 })
                {
                    MessageBox.Show(this, "Could not encode the cover image.", "Edit album",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ResultCover = encoded;
            }
        }

        DialogResult = true;
        Close();
    }
}
