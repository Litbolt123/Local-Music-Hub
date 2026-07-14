using System.Text.RegularExpressions;

namespace LocalMusicHub.Services;

public static partial class YouTubeUrlHelper
{
    [GeneratedRegex(@"^(https?://)?(www\.)?(youtube\.com|youtu\.be|music\.youtube\.com)/", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeHostPattern();

    public static bool IsYouTubeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        return YouTubeHostPattern().IsMatch(url.Trim());
    }

    public static string? TryGetVideoId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var trimmed = url.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            if (uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
            {
                var id = uri.AbsolutePath.Trim('/').Split('?')[0];
                return string.IsNullOrWhiteSpace(id) ? null : id;
            }

            if (uri.Host.Contains("youtube", StringComparison.OrdinalIgnoreCase))
            {
                var v = GetQueryValue(uri.Query, "v");
                if (!string.IsNullOrWhiteSpace(v))
                    return v;
            }
        }

        return null;
    }

    private static string? GetQueryValue(string query, string key)
    {
        if (string.IsNullOrEmpty(query))
            return null;

        var trimmed = query.StartsWith('?') ? query[1..] : query;
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 &&
                string.Equals(Uri.UnescapeDataString(pair[0]), key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pair[1]);
        }

        return null;
    }

    public static bool UrlsMatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;

        if (string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        var idA = TryGetVideoId(a);
        var idB = TryGetVideoId(b);
        return idA is not null && idB is not null &&
               string.Equals(idA, idB, StringComparison.OrdinalIgnoreCase);
    }
}
