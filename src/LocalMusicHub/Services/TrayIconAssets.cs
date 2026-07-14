using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;

namespace LocalMusicHub.Services;

internal static class TrayIconAssets
{
    public static Icon CreateIcon()
    {
        try
        {
            var stream = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"))?.Stream;
            if (stream is not null)
            {
                using (stream)
                {
                    using var icon = new Icon(stream);
                    return (Icon)icon.Clone();
                }
            }
        }
        catch
        {
            /* fall through */
        }

        return SystemIcons.Application;
    }

    public static BitmapSource CreateWindowIcon()
    {
        try
        {
            var stream = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"))?.Stream;
            if (stream is not null)
            {
                using (stream)
                {
                    var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    frame.Freeze();
                    return frame;
                }
            }
        }
        catch
        {
            /* fall through */
        }

        using var icon = CreateIcon();
        using var mem = new MemoryStream();
        icon.Save(mem);
        mem.Position = 0;
        var drawn = BitmapFrame.Create(mem, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        drawn.Freeze();
        return drawn;
    }
}
