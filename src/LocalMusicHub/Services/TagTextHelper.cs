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

        if (text.Contains(Replacement) && !string.IsNullOrWhiteSpace(filePath))
        {
            var fromFile = TitleFromAudioFileName(filePath);
            if (fromFile.Length > 0 && !fromFile.Contains(Replacement))
                return fromFile;
        }

        return text;
    }

    /// <summary>
    /// YouTube/FLAC TITLE is often U+FFFD while DESCRIPTION still has the real UTF-8 title
    /// (e.g. "Oxyg�ne" vs "Oxygène", "�quinoxe" vs "Équinoxe").
    /// </summary>
    public static string RecoverReplacementTitle(string corrupted, string? haystack)
    {
        if (string.IsNullOrWhiteSpace(haystack) || !corrupted.Contains(Replacement))
            return "";

        var escaped = Regex.Escape(corrupted).Replace(Replacement.ToString(), @"\p{L}");
        Regex rx;
        try
        {
            rx = new Regex("^" + escaped + "$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
        }
        catch
        {
            return "";
        }

        foreach (var raw in haystack.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.Contains(Replacement))
                continue;
            if (rx.IsMatch(line))
                return line;

            var beforeSeparator = line.Split(['·', '-', '|'], 2)[0].Trim();
            if (beforeSeparator.Length > 0 && rx.IsMatch(beforeSeparator))
                return beforeSeparator;
        }

        return "";
    }

    private static string TitleFromAudioFileName(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(name))
            return "";

        if (!name.Contains(Replacement))
        {
            var match = TrackFileNameRegex().Match(name);
            return match.Success ? match.Groups[1].Value.Trim() : name.Trim();
        }

        return name.Trim();
    }

    private static string FixLatin1Mojibake(string value)
    {
        if (!value.Contains('Ã') && !value.Contains('â') && !value.Contains(Replacement))
            return value;

        foreach (var encodingName in new[] { "ISO-8859-1", "windows-1252" })
        {
            try
            {
                var bytes = Encoding.GetEncoding(encodingName).GetBytes(value);
                var utf8 = Encoding.UTF8.GetString(bytes);
                if (!utf8.Contains(Replacement) && utf8.Any(static c => c > 127))
                    return utf8;
            }
            catch
            {
                /* ignore */
            }
        }

        return value;
    }

    [GeneratedRegex(@"^\s*\d+\s*[.\-_\s]+\s*(.+)$", RegexOptions.Compiled)]
    private static partial Regex TrackFileNameRegex();
}
