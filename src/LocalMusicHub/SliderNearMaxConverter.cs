using System.Globalization;
using System.Windows.Data;

namespace LocalMusicHub;

/// <summary>True when a slider value is at or effectively at its maximum (avoids float drift at 1.0).</summary>
public sealed class SliderNearMaxConverter : IValueConverter
{
    private const double DefaultThreshold = 0.995;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double d)
            return false;

        var threshold = DefaultThreshold;
        if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
            threshold = t;

        return d >= threshold;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
