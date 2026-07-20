using System.Globalization;
using System.Windows;
using Application = System.Windows.Application;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace LocalMusicHub.Services;

public sealed record AccentPreset(
    string Id,
    string DisplayName,
    byte R,
    byte G,
    byte B,
    byte HoverR,
    byte HoverG,
    byte HoverB,
    byte PrimaryFgR,
    byte PrimaryFgG,
    byte PrimaryFgB);

public static class HubTheme
{
    public const string AccentPurple = "purple";
    public const string AccentSpotify = "spotify";
    public const string AccentOcean = "ocean";
    public const string AccentRose = "rose";
    public const string AccentAmber = "amber";
    public const string AccentCrimson = "crimson";
    public const string AccentTeal = "teal";
    public const string AccentSunset = "sunset";
    public const string AccentCustom = "custom";

    public static event EventHandler? ThemeChanged;

    public static readonly IReadOnlyList<AccentPreset> AccentPresets =
    [
        new(AccentPurple, "Purple (classic)", 0x7C, 0x4D, 0xFF, 0x9B, 0x6B, 0xFF, 0xFF, 0xFF, 0xFF),
        new(AccentSpotify, "Spotify green", 0x1D, 0xB9, 0x54, 0x1E, 0xD7, 0x60, 0x12, 0x12, 0x12),
        new(AccentOcean, "Ocean blue", 0x00, 0x96, 0xC8, 0x00, 0xB4, 0xDB, 0xFF, 0xFF, 0xFF),
        new(AccentTeal, "Teal", 0x14, 0xB8, 0xA6, 0x2D, 0xD4, 0xBF, 0xFF, 0xFF, 0xFF),
        new(AccentRose, "Rose", 0xE9, 0x1E, 0x63, 0xF4, 0x8F, 0xB1, 0xFF, 0xFF, 0xFF),
        new(AccentCrimson, "Crimson", 0xDC, 0x26, 0x26, 0xEF, 0x44, 0x44, 0xFF, 0xFF, 0xFF),
        new(AccentAmber, "Amber", 0xFF, 0xB3, 0x00, 0xFF, 0xCA, 0x28, 0x12, 0x12, 0x12),
        new(AccentSunset, "Sunset orange", 0xF9, 0x73, 0x16, 0xFB, 0x92, 0x3C, 0x12, 0x12, 0x12),
        new(AccentCustom, "Custom color…", 0x7C, 0x4D, 0xFF, 0x9B, 0x6B, 0xFF, 0xFF, 0xFF, 0xFF),
    ];

    public static readonly Uri LightThemeUri = new("Themes/HubThemeLight.xaml", UriKind.Relative);
    public static readonly Uri DarkThemeUri = new("Themes/HubThemeDark.xaml", UriKind.Relative);

    public static void ApplyFromSettings() =>
        Apply(App.Settings.UseDarkTheme, App.Settings.AccentTheme, App.Settings.CustomAccentColor);

    public static void Apply(bool dark, string? accentTheme = null, string? customAccentHex = null)
    {
        var app = Application.Current;
        if (app is null)
            return;

        var target = dark ? DarkThemeUri : LightThemeUri;
        var merged = app.Resources.MergedDictionaries;
        for (var i = merged.Count - 1; i >= 0; i--)
        {
            if (merged[i].Source == LightThemeUri || merged[i].Source == DarkThemeUri)
                merged.RemoveAt(i);
        }

        merged.Insert(0, new ResourceDictionary { Source = target });
        ApplyAccent(accentTheme, customAccentHex);
    }

    public static void ApplyAccent(string? accentTheme, string? customAccentHex = null)
    {
        var app = Application.Current;
        if (app is null)
            return;

        var hex = customAccentHex ?? App.Settings.CustomAccentColor;
        var (accent, hover, primaryFg) = ResolveAccent(accentTheme, hex);
        app.Resources["HubAccentColor"] = accent;
        app.Resources["HubAccentHoverColor"] = hover;
        app.Resources["HubPrimaryForegroundColor"] = primaryFg;
        app.Resources["HubAccentBrush"] = Freeze(new SolidColorBrush(accent));
        app.Resources["HubAccentHoverBrush"] = Freeze(new SolidColorBrush(hover));
        app.Resources["HubPrimaryForegroundBrush"] = Freeze(new SolidColorBrush(primaryFg));
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string NormalizeAccent(string? accentTheme)
    {
        if (string.IsNullOrWhiteSpace(accentTheme))
            return AccentPurple;

        if (string.Equals(accentTheme, AccentCustom, StringComparison.OrdinalIgnoreCase))
            return AccentCustom;

        foreach (var preset in AccentPresets)
        {
            if (string.Equals(preset.Id, accentTheme, StringComparison.OrdinalIgnoreCase))
                return preset.Id;
        }

        return AccentPurple;
    }

    public static string FormatColorHex(MediaColor color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static MediaColor? TryParseColorHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        var value = hex.Trim();
        if (!value.StartsWith('#'))
            value = "#" + value;

        try
        {
            var converted = System.Windows.Media.ColorConverter.ConvertFromString(value);
            return converted is MediaColor color ? color : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Ensure(Window window)
    {
        ApplyFromSettings();
        window.Background = Application.Current.TryFindResource("HubBgBrush") as MediaBrush
                            ?? MediaBrushes.Transparent;
        try { window.Icon = TrayIconAssets.CreateWindowIcon(); } catch { /* ignore */ }
    }

    private static (MediaColor Accent, MediaColor Hover, MediaColor PrimaryFg) ResolveAccent(
        string? accentTheme,
        string? customAccentHex)
    {
        var id = NormalizeAccent(accentTheme);
        if (id == AccentCustom)
        {
            var custom = TryParseColorHex(customAccentHex) ?? MediaColor.FromRgb(0x7C, 0x4D, 0xFF);
            return (custom, Lighten(custom, 0.14), ContrastForeground(custom));
        }

        var preset = AccentPresets.First(p => p.Id == id);
        return (
            MediaColor.FromRgb(preset.R, preset.G, preset.B),
            MediaColor.FromRgb(preset.HoverR, preset.HoverG, preset.HoverB),
            MediaColor.FromRgb(preset.PrimaryFgR, preset.PrimaryFgG, preset.PrimaryFgB));
    }

    private static MediaColor Lighten(MediaColor color, double amount)
    {
        byte Blend(byte channel) =>
            (byte)Math.Clamp(channel + (255 - channel) * amount, 0, 255);

        return MediaColor.FromRgb(Blend(color.R), Blend(color.G), Blend(color.B));
    }

    private static MediaColor ContrastForeground(MediaColor background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance > 0.58
            ? MediaColor.FromRgb(0x12, 0x12, 0x12)
            : MediaColor.FromRgb(0xFF, 0xFF, 0xFF);
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
