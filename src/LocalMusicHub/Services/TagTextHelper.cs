using System.Text;
using System.Text.RegularExpressions;

namespace LocalMusicHub.Services;

public static partial class TagTextHelper
{
    private const char Replacement = '\uFFFD';

    public static string Clean(string? value, string? filePath = null, bool titleFromFileName = false)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        if (text.Length == 0)
            return text;

        text = FixLatin1Mojibake(text);

        if (text.Contains(Replacement) && titleFromFileName && !string.IsNullOrWhiteSpace(filePath))
        {
            var fromFile = TitleFromAudioFileName(filePath);
            if (fromFile.Length > 0 && !fromFile.Contains(Replacement))
                return fromFile;
        }

        return text;
    }

    private static string TitleFromAudioFileName(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var match = TrackFileNameRegex().Match(name);
        return match.Success ? match.Groups[1].Value.Trim() : name.Trim();
    }

    private static string FixLatin1Mojibake(string value)
    {
        if (!value.Contains('Ã') && !value.Contains('â') && !value.Contains(Replacement))
            return value;

        try
        {
            var bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(value);
            var utf8 = Encoding.UTF8.GetString(bytes);
            if (!utf8.Contains(Replacement) && utf8.Any(static c => c > 127))
                return utf8;
        }
        catch
        {
            /* ignore */
        }

        return value;
    }

    [GeneratedRegex(@"^\s*\d+\s*[-._]\s*(.+)$", RegexOptions.Compiled)]
    private static partial Regex TrackFileNameRegex();
}
