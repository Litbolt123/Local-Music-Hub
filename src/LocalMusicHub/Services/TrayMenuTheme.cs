using System.Drawing;
using System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace LocalMusicHub.Services;

/// <summary>
/// WinForms tray context menu colors aligned with HubThemeDark / HubThemeLight and accent.
/// </summary>
internal static class TrayMenuTheme
{
    private static readonly TrayMenuPalette Dark = new(
        Background: Color.FromArgb(0x18, 0x18, 0x18),
        Foreground: Color.FromArgb(0xFF, 0xFF, 0xFF),
        Hover: Color.FromArgb(0x2A, 0x2A, 0x2A),
        Selected: Color.FromArgb(0x33, 0x33, 0x33),
        Border: Color.FromArgb(0x2A, 0x2A, 0x2A),
        Separator: Color.FromArgb(0x2A, 0x2A, 0x2A));

    private static readonly TrayMenuPalette Light = new(
        Background: Color.FromArgb(0xFF, 0xFF, 0xFF),
        Foreground: Color.FromArgb(0x12, 0x12, 0x12),
        Hover: Color.FromArgb(0xE8, 0xE8, 0xE8),
        Selected: Color.FromArgb(0xDC, 0xDC, 0xDC),
        Border: Color.FromArgb(0xE6, 0xE6, 0xE6),
        Separator: Color.FromArgb(0xE6, 0xE6, 0xE6));

    public static void Apply(ContextMenuStrip menu)
    {
        var palette = BuildPalette();

        menu.BackColor = palette.Background;
        menu.ForeColor = palette.Foreground;
        menu.RenderMode = ToolStripRenderMode.Professional;
        menu.Renderer = new HubTrayMenuRenderer(palette);
        menu.ShowImageMargin = false;
        menu.Font = new Font("Segoe UI", 9f);

        ApplyToItems(menu.Items, palette);
    }

    private static TrayMenuPalette BuildPalette()
    {
        var basePalette = App.Settings.UseDarkTheme ? Dark : Light;
        var accent = TryGetAccentColor();
        if (accent is null)
            return basePalette;

        return basePalette with
        {
            Hover = Blend(accent.Value, basePalette.Background, 0.28),
            Selected = Blend(accent.Value, basePalette.Background, 0.42),
        };
    }

    private static MediaColor? TryGetAccentColor()
    {
        try
        {
            if (System.Windows.Application.Current?.TryFindResource("HubAccentColor") is MediaColor color)
                return color;
        }
        catch
        {
            /* ignore */
        }

        return null;
    }

    private static Color Blend(MediaColor accent, Color background, double amount)
    {
        byte Mix(byte accentChannel, byte backgroundChannel) =>
            (byte)Math.Clamp(backgroundChannel + (accentChannel - backgroundChannel) * amount, 0, 255);

        return Color.FromArgb(
            Mix(accent.R, background.R),
            Mix(accent.G, background.G),
            Mix(accent.B, background.B));
    }

    private static void ApplyToItems(ToolStripItemCollection items, TrayMenuPalette palette)
    {
        foreach (ToolStripItem item in items)
        {
            item.BackColor = palette.Background;
            item.ForeColor = palette.Foreground;

            if (item is ToolStripMenuItem menuItem && menuItem.HasDropDownItems)
                ApplyToItems(menuItem.DropDownItems, palette);
        }
    }

    private readonly record struct TrayMenuPalette(
        Color Background,
        Color Foreground,
        Color Hover,
        Color Selected,
        Color Border,
        Color Separator);

    private sealed class HubTrayMenuRenderer : ToolStripProfessionalRenderer
    {
        public HubTrayMenuRenderer(TrayMenuPalette palette)
            : base(new HubTrayColorTable(palette))
        {
            RoundedEdges = false;
        }
    }

    private sealed class HubTrayColorTable : ProfessionalColorTable
    {
        private readonly TrayMenuPalette _palette;

        public HubTrayColorTable(TrayMenuPalette palette) => _palette = palette;

        public override Color MenuBorder => _palette.Border;
        public override Color MenuItemBorder => _palette.Border;
        public override Color MenuItemSelected => _palette.Hover;
        public override Color MenuItemSelectedGradientBegin => _palette.Hover;
        public override Color MenuItemSelectedGradientEnd => _palette.Hover;
        public override Color MenuItemPressedGradientBegin => _palette.Selected;
        public override Color MenuItemPressedGradientEnd => _palette.Selected;
        public override Color ToolStripDropDownBackground => _palette.Background;
        public override Color ImageMarginGradientBegin => _palette.Background;
        public override Color ImageMarginGradientMiddle => _palette.Background;
        public override Color ImageMarginGradientEnd => _palette.Background;
        public override Color ImageMarginRevealedGradientBegin => _palette.Background;
        public override Color ImageMarginRevealedGradientMiddle => _palette.Background;
        public override Color ImageMarginRevealedGradientEnd => _palette.Background;
        public override Color SeparatorDark => _palette.Separator;
        public override Color SeparatorLight => _palette.Separator;
        public override Color MenuStripGradientBegin => _palette.Background;
        public override Color MenuStripGradientEnd => _palette.Background;
        public override Color ToolStripGradientBegin => _palette.Background;
        public override Color ToolStripGradientMiddle => _palette.Background;
        public override Color ToolStripGradientEnd => _palette.Background;
        public override Color OverflowButtonGradientBegin => _palette.Background;
        public override Color OverflowButtonGradientMiddle => _palette.Background;
        public override Color OverflowButtonGradientEnd => _palette.Background;
        public override Color ButtonSelectedBorder => _palette.Border;
        public override Color ButtonPressedBorder => _palette.Border;
        public override Color CheckBackground => _palette.Hover;
        public override Color CheckPressedBackground => _palette.Selected;
        public override Color CheckSelectedBackground => _palette.Hover;
        public override Color GripDark => _palette.Separator;
        public override Color GripLight => _palette.Separator;
    }
}
