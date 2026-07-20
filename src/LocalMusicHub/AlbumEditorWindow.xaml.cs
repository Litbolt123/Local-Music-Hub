using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using LocalMusicHub.Models;
using LocalMusicHub.Services;
using MessageBox = System.Windows.MessageBox;

namespace LocalMusicHub;

public sealed class AlbumTrackEditRow
{
    public long Id { get; init; }
    public bool IsEditable { get; init; } = true;
    public string TrackNumberText { get; set; } = "";
    public int? TrackNumber { get; set; }
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public int Rating { get; set; }
    public string DurationLabel { get; init; } = "";
}

public partial class AlbumEditorWindow
{
    private readonly LibraryTrack _seedTrack;
    private readonly IReadOnlyList<LibraryTrack> _albumTracks;
    private readonly byte[]? _originalCover;
    private byte[]? _sourceBytes;
    private bool _removeCover;

    public ObservableCollection<AlbumTrackEditRow> TrackRows { get; } = [];

    public string? ResultAlbumArtist { get; private set; }
    public string? ResultAlbum { get; private set; }
    public int? ResultYear { get; private set; }
    public string? ResultGenre { get; private set; }
    public string? ResultDateReleased { get; private set; }
    public string? ResultComment { get; private set; }
    public IReadOnlyList<AlbumTrackEditRow> ResultTracks { get; private set; } = [];
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
        DataContext = this;

        _seedTrack = seedTrack;
        _albumTracks = albumTracks.Count > 0 ? albumTracks : [seedTrack];
        _originalCover = seedTrack.CoverArt;
        _sourceBytes = seedTrack.CoverArt;

        AlbumArtistBox.Text = albumArtist;
        AlbumBox.Text = album;
        YearBox.Text = ResolveAlbumYear(_albumTracks)?.ToString() ?? "";
        GenreBox.Text = ResolveAlbumGenre(_albumTracks);
        DateReleasedBox.Text = ResolveAlbumDateReleased(_albumTracks);
        CommentsBox.Text = ResolveAlbumComment(_albumTracks);
        SubtitleText.Text = $"{albumArtist} — {album}";
        TrackCountText.Text =
            $"Album fields apply to all {_albumTracks.Count} track(s). Set per-track ratings below.";

        foreach (var track in _albumTracks
                     .OrderBy(t => t.TrackNumber ?? int.MaxValue)
                     .ThenBy(t => t.DisplayTitle, StringComparer.OrdinalIgnoreCase))
        {
            var isCue = track.FilePath.Contains(CuePathHelper.CueSuffix, StringComparison.Ordinal);
            TrackRows.Add(new AlbumTrackEditRow
            {
                Id = track.Id,
                IsEditable = !isCue,
                TrackNumberText = track.TrackNumber?.ToString() ?? "",
                TrackNumber = track.TrackNumber,
                Title = track.Title,
                Artist = track.Artist,
                Rating = Math.Clamp(track.Rating, 0, 5),
                DurationLabel = track.DurationLabel,
            });
        }

        RefreshPreview();
    }

    private static int? ResolveAlbumYear(IReadOnlyList<LibraryTrack> tracks)
    {
        var years = tracks.Where(t => t.Year is > 0).Select(t => t.Year!.Value).ToList();
        if (years.Count == 0)
            return null;
        return years.GroupBy(y => y).OrderByDescending(g => g.Count()).First().Key;
    }

    private static string ResolveAlbumGenre(IReadOnlyList<LibraryTrack> tracks)
    {
        var genres = tracks.Where(t => !string.IsNullOrWhiteSpace(t.Genre)).Select(t => t.Genre.Trim()).ToList();
        if (genres.Count == 0)
            return "";
        return genres.GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .First()
            .Key;
    }

    private static string ResolveAlbumDateReleased(IReadOnlyList<LibraryTrack> tracks)
    {
        var values = tracks
            .Select(t => string.IsNullOrWhiteSpace(t.DateReleased) ? null : t.DateReleased.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
        if (values.Count > 0)
        {
            return values.GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .First()
                .Key ?? "";
        }

        var sample = tracks.FirstOrDefault(t => !t.FilePath.Contains(CuePathHelper.CueSuffix, StringComparison.Ordinal));
        return sample is null ? "" : AudioTagReader.ReadDateReleased(sample.AudioFilePath);
    }

    private static string ResolveAlbumComment(IReadOnlyList<LibraryTrack> tracks)
    {
        var values = tracks
            .Select(t => string.IsNullOrWhiteSpace(t.Comment) ? null : t.Comment.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
        if (values.Count > 0)
        {
            return values.GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .First()
                .Key ?? "";
        }

        var sample = tracks.FirstOrDefault(t => !t.FilePath.Contains(CuePathHelper.CueSuffix, StringComparison.Ordinal));
        return sample is null ? "" : AudioTagReader.ReadComment(sample.AudioFilePath);
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
        _sourceBytes = CoverArtHelper.NormalizeDownloadedCover(bytes) ?? bytes;
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

        int? year = null;
        if (!string.IsNullOrWhiteSpace(YearBox.Text))
        {
            if (!int.TryParse(YearBox.Text.Trim(), out var parsedYear) || parsedYear < 0)
            {
                MessageBox.Show(this, "Year must be a non-negative integer.", "Edit album",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            year = parsedYear == 0 ? null : parsedYear;
        }

        foreach (var row in TrackRows.Where(r => r.IsEditable))
        {
            if (!TryParseTrackNumber(row, out var error))
            {
                MessageBox.Show(this, error, "Edit album", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(row.Title))
            {
                MessageBox.Show(this, "Every track needs a title.", "Edit album",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        ResultAlbumArtist = AlbumArtistBox.Text.Trim();
        ResultAlbum = album;
        ResultYear = year;
        ResultGenre = GenreBox.Text.Trim();
        ResultDateReleased = DateReleasedBox.Text.Trim();
        ResultComment = CommentsBox.Text.Trim();
        ResultTracks = TrackRows.ToList();
        UpdateCover = UpdateCoverBox.IsChecked == true;
        ClearCover = UpdateCover && _removeCover;

        if (UpdateCover && !_removeCover)
        {
            if (_sourceBytes is not { Length: > 0 })
            {
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

    private static bool TryParseTrackNumber(AlbumTrackEditRow row, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(row.TrackNumberText))
        {
            row.TrackNumber = null;
            return true;
        }

        if (!int.TryParse(row.TrackNumberText.Trim(), out var trackNumber) || trackNumber < 0)
        {
            error = $"Track number must be a non-negative integer (row: {row.Title}).";
            return false;
        }

        row.TrackNumber = trackNumber == 0 ? null : trackNumber;
        return true;
    }
}
