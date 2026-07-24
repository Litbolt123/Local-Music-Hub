using System.Globalization;
using System.Windows.Data;

namespace LocalMusicHub;

/// <summary>Maps slider Value into a fill Width = trackActualWidth * normalizedValue (Spotify-style full fill).</summary>
public sealed class SliderFillWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 4)
            return 0.0;

        if (values[0] is not double width ||
            values[1] is not double value ||
            values[2] is not double min ||
            values[3] is not double max)
            return 0.0;

        if (width <= 0 || max <= min || double.IsNaN(width) || double.IsNaN(value))
            return 0.0;

        var t = Math.Clamp((value - min) / (max - min), 0, 1);
        return width * t;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
