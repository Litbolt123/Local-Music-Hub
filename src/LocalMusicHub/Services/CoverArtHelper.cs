using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LocalMusicHub.Services;

public static class CoverArtHelper
{
    /// <param name="offsetX">-1 (left) to +1 (right); 0 = centered.</param>
    /// <param name="offsetY">-1 (top) to +1 (bottom); 0 = centered.</param>
    /// <param name="zoom">1 = full square crop; higher values zoom in (tighter crop).</param>
    public static BitmapSource? ToBitmap(
        byte[]? data,
        int decodePixelWidth = 0,
        double offsetX = 0,
        double offsetY = 0,
        bool centerCropSquare = true,
        double zoom = 1.0)
    {
        if (data is not { Length: > 0 })
            return null;

        try
        {
            var decodeMax = decodePixelWidth > 0
                ? (int)Math.Ceiling(decodePixelWidth * Math.Max(zoom, 1.0) * 1.5)
                : 640;
            var source = LoadBitmap(data, decodeMax);
            if (source is null)
                return null;

            if (centerCropSquare)
                source = CropSquare(source, offsetX, offsetY, zoom);

            if (decodePixelWidth > 0 && source.PixelWidth != decodePixelWidth)
                source = ScaleToWidth(source, decodePixelWidth);

            if (source.CanFreeze)
                source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Downscale/normalize online cover art so WPF decode and tag embed stay stable.</summary>
    public static byte[]? NormalizeDownloadedCover(byte[]? data, int outputSize = 1600, int quality = 92)
    {
        if (data is not { Length: > 0 })
            return null;

        return EncodeJpegSquare(data, outputSize: outputSize, quality: quality)
               ?? EncodeJpegSquare(data, outputSize: 1000, quality: 88);
    }

    public static void WarmAlbumThumbnails(IEnumerable<Models.LibraryAlbum> albums, int width = 150)
    {
        foreach (var album in albums)
        {
            if (album.CoverArt is { Length: > 0 })
                album.CoverThumbnail = ToBitmap(album.CoverArt, width, centerCropSquare: true);
        }
    }

    public static byte[]? EncodeJpegSquare(
        byte[]? data,
        double offsetX = 0,
        double offsetY = 0,
        int outputSize = 600,
        int quality = 90,
        double zoom = 1.0)
    {
        var square = ToBitmap(data, outputSize, offsetX, offsetY, centerCropSquare: true, zoom: zoom);
        if (square is null)
            return null;

        try
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 50, 100) };
            encoder.Frames.Add(BitmapFrame.Create(square));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? RotateJpegOrPng(byte[]? data, int degreesClockwise)
    {
        if (data is not { Length: > 0 })
            return null;

        degreesClockwise = ((degreesClockwise % 360) + 360) % 360;
        if (degreesClockwise == 0)
            return data;

        try
        {
            var source = LoadBitmap(data);
            if (source is null)
                return null;

            var rotated = new TransformedBitmap(source, new RotateTransform(degreesClockwise));
            rotated.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rotated));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? TryGetClipboardImage()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsImage())
                return null;

            var image = System.Windows.Clipboard.GetImage();
            if (image is null)
                return null;

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? LoadImageFile(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch
        {
            return null;
        }
    }

    public static void WriteCoverToFile(string audioPath, byte[] jpegBytes)
    {
        jpegBytes = NormalizeDownloadedCover(jpegBytes, outputSize: 1200, quality: 90) ?? jpegBytes;
        WriteWithFileRetry(audioPath, () =>
        {
            using var file = TagLib.File.Create(audioPath);
            StripId3IfXiphContainer(file, audioPath);
            var picture = new TagLib.Picture(new TagLib.ByteVector(jpegBytes))
            {
                Type = TagLib.PictureType.FrontCover,
                MimeType = "image/jpeg",
                Description = "Cover",
            };
            file.Tag.Pictures = [picture];
            file.Save();
        });
    }

    public static void ClearCoverFromFile(string audioPath)
    {
        WriteWithFileRetry(audioPath, () =>
        {
            using var file = TagLib.File.Create(audioPath);
            StripId3IfXiphContainer(file, audioPath);
            file.Tag.Pictures = [];
            file.Save();
        });
    }

    private static void StripId3IfXiphContainer(TagLib.File file, string audioPath)
    {
        var ext = Path.GetExtension(audioPath);
        if (!ext.Equals(".flac", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".oga", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".opus", StringComparison.OrdinalIgnoreCase))
            return;

        file.RemoveTags(TagLib.TagTypes.Id3v1 | TagLib.TagTypes.Id3v2);
        _ = file.GetTag(TagLib.TagTypes.Xiph, create: true);
    }

    private static void WriteWithFileRetry(string audioPath, Action write, int attempts = 5)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                if (!AudioFileAccess.WaitUntilReadableAsync(audioPath, TimeSpan.FromSeconds(12))
                        .GetAwaiter().GetResult())
                {
                    throw new IOException($"Audio file is not ready: {audioPath}");
                }

                write();
                return;
            }
            catch (Exception ex) when (AudioFileAccess.IsSharingViolation(ex) && attempt < attempts - 1)
            {
                last = ex;
                Thread.Sleep(250 * (attempt + 1));
            }
        }

        throw last ?? new IOException($"Could not update cover art: {audioPath}");
    }

    private static BitmapSource? LoadBitmap(byte[] data, int decodePixelWidth = 0)
    {
        using var ms = new MemoryStream(data);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (decodePixelWidth > 0)
            image.DecodePixelWidth = decodePixelWidth;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static BitmapSource CropSquare(BitmapSource source, double offsetX, double offsetY, double zoom)
    {
        var w = source.PixelWidth;
        var h = source.PixelHeight;
        if (w <= 0 || h <= 0)
            return source;

        zoom = Math.Clamp(zoom, 1.0, 4.0);
        var side = (int)Math.Round(Math.Min(w, h) / zoom);
        side = Math.Clamp(side, 1, Math.Min(w, h));

        if (side == w && side == h && Math.Abs(offsetX) < 0.001 && Math.Abs(offsetY) < 0.001)
            return source;

        var maxX = w - side;
        var maxY = h - side;
        var x = maxX / 2.0 + Math.Clamp(offsetX, -1, 1) * (maxX / 2.0);
        var y = maxY / 2.0 + Math.Clamp(offsetY, -1, 1) * (maxY / 2.0);
        x = Math.Clamp(Math.Round(x), 0, maxX);
        y = Math.Clamp(Math.Round(y), 0, maxY);

        var cropped = new CroppedBitmap(source, new Int32Rect((int)x, (int)y, side, side));
        cropped.Freeze();
        return cropped;
    }

    private static BitmapSource ScaleToWidth(BitmapSource source, int width)
    {
        if (source.PixelWidth <= 0)
            return source;

        var scale = width / (double)source.PixelWidth;
        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    /// <summary>2×2 mosaic from up to four cover blobs (playlist thumbnails). Must run on the UI thread.</summary>
    public static BitmapSource? BuildMosaic(IReadOnlyList<byte[]?> covers, int outputSize = 96)
    {
        // Always decode to equal squares so DrawImage never squash-stretches art into tall strips.
        var tileSize = Math.Max(8, outputSize / 2);
        var tiles = covers
            .Where(c => c is { Length: > 0 })
            .Take(4)
            .Select(c => ToBitmap(c, tileSize, centerCropSquare: true))
            .Where(s => s is not null)
            .Select(s => NormalizeTo96Dpi(s!))
            .ToList();
        if (tiles.Count == 0)
            return null;
        if (tiles.Count == 1)
            return ToBitmap(covers.First(c => c is { Length: > 0 }), outputSize, centerCropSquare: true);

        // RenderTargetBitmap / DrawingVisual require the WPF UI thread.
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher &&
            !dispatcher.CheckAccess())
        {
            return dispatcher.Invoke(() => BuildMosaic(covers, outputSize));
        }

        var size = outputSize;
        var half = size / 2;
        // Pad to a full 2×2 so 2–3 covers still fill the square (no tall side-by-side strips).
        var cell = tiles.Count switch
        {
            2 => new[] { tiles[0], tiles[1], tiles[0], tiles[1] },
            3 => new[] { tiles[0], tiles[1], tiles[2], tiles[2] },
            _ => new[] { tiles[0], tiles[1], tiles[2], tiles[3] },
        };

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(cell[0], new Rect(0, 0, half, half));
            dc.DrawImage(cell[1], new Rect(half, 0, half, half));
            dc.DrawImage(cell[2], new Rect(0, half, half, half));
            dc.DrawImage(cell[3], new Rect(half, half, half, half));
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>DrawImage uses DIPs; non-96 DPI bitmaps can render as thin strips inside the mosaic.</summary>
    private static BitmapSource NormalizeTo96Dpi(BitmapSource source)
    {
        if (Math.Abs(source.DpiX - 96) < 0.5 && Math.Abs(source.DpiY - 96) < 0.5)
            return source;

        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.CopyPixels(pixels, stride, 0);
        var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        wb.Freeze();
        return wb;
    }

    public static byte[]? EncodeMosaicPng(IReadOnlyList<byte[]?> covers, int outputSize = 96)
    {
        var mosaic = BuildMosaic(covers, outputSize);
        if (mosaic is null)
            return null;

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(mosaic));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
