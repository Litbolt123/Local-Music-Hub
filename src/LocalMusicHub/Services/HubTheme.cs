using System.Windows;
using Application = System.Windows.Application;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace LocalMusicHub.Services;

public static class HubTheme
{
    public const string AccentPurple = "purple";
    public const string AccentSpotify = "spotify";

    public static readonly Uri LightThemeUri = new("Themes/HubThemeLight.xaml", UriKind.Relative);
    public static readonly Uri DarkThemeUri = new("Themes/HubThemeDark.xaml", UriKind.Relative);

    public static void ApplyFromSettings() =>
        Apply(App.Settings.UseDarkTheme, App.Settings.AccentTheme);

    public static void Apply(bool dark, string? accentTheme = null)
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
        ApplyAccent(accentTheme ?? App.Settings.AccentTheme);
    }

    public static void ApplyAccent(string? accentTheme)
    {
        var app = Application.Current;
        if (app is null)
            return;

        var (accent, hover, primaryFg) = ResolveAccent(accentTheme);
        app.Resources["HubAccentColor"] = accent;
        app.Resources["HubAccentHoverColor"] = hover;
        app.Resources["HubPrimaryForegroundColor"] = primaryFg;
        app.Resources["HubAccentBrush"] = new SolidColorBrush(accent);
        app.Resources["HubAccentHoverBrush"] = new SolidColorBrush(hover);
        app.Resources["HubPrimaryForegroundBrush"] = new SolidColorBrush(primaryFg);
    }

    public static string NormalizeAccent(string? accentTheme) =>
        string.Equals(accentTheme, AccentSpotify, StringComparison.OrdinalIgnoreCase)
            ? AccentSpotify
            : AccentPurple;

    public static void Ensure(Window window)
    {
        ApplyFromSettings();
        window.Background = Application.Current.TryFindResource("HubBgBrush") as MediaBrush
                            ?? MediaBrushes.Transparent;
        try { window.Icon = TrayIconAssets.CreateWindowIcon(); } catch { /* ignore */ }
    }

    private static (MediaColor Accent, MediaColor Hover, MediaColor PrimaryFg) ResolveAccent(string? accentTheme)
    {
        if (NormalizeAccent(accentTheme) == AccentSpotify)
            return (MediaColor.FromRgb(0x1D, 0xB9, 0x54), MediaColor.FromRgb(0x1E, 0xD7, 0x60), MediaColor.FromRgb(0x12, 0x12, 0x12));

        return (MediaColor.FromRgb(0x7C, 0x4D, 0xFF), MediaColor.FromRgb(0x9B, 0x6B, 0xFF), MediaColor.FromRgb(0xFF, 0xFF, 0xFF));
    }
}
