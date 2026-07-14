using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using LocalMusicHub.Services;

namespace LocalMusicHub;

public sealed class CoverArtToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var width = 0;
        if (parameter is string s && int.TryParse(s, out var w))
            width = w;
        return CoverArtHelper.ToBitmap(value as byte[], width, centerCropSquare: true);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
