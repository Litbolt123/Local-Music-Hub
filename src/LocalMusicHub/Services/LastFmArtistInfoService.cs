using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;

namespace LocalMusicHub.Services;

public sealed class ArtistInfoResult
{
    public string Artist { get; init; } = "";
    public string? Bio { get; init; }
    public string? ImageUrl { get; init; }
    public string Source { get; init; } = "Local library";
}

public static class LastFmArtistInfoService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public static async Task<ArtistInfoResult> GetArtistInfoAsync(string artist, CancellationToken cancellationToken = default)
    {
        var settings = App.Settings;
        if (string.IsNullOrWhiteSpace(artist))
            return new ArtistInfoResult { Artist = artist };

        if (string.IsNullOrWhiteSpace(settings.LastFmApiKey))
            return new ArtistInfoResult { Artist = artist, Source = "Local library" };

        try
        {
            var url =
                "https://ws.audioscrobbler.com/2.0/?method=artist.getinfo" +
                $"&artist={Uri.EscapeDataString(artist)}" +
                $"&api_key={Uri.EscapeDataString(settings.LastFmApiKey)}" +
                "&format=json";
            using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new ArtistInfoResult { Artist = artist, Source = "Local library" };

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;
            if (!root.TryGetProperty("artist", out var artistNode))
                return new ArtistInfoResult { Artist = artist, Source = "Local library" };

            string? bio = null;
            if (artistNode.TryGetProperty("bio", out var bioNode) &&
                bioNode.TryGetProperty("summary", out var summaryNode))
            {
                bio = StripTags(summaryNode.GetString());
            }

            string? image = null;
            if (artistNode.TryGetProperty("image", out var images) && images.ValueKind == JsonValueKind.Array)
            {
                foreach (var img in images.EnumerateArray().Reverse())
                {
                    var text = img.TryGetProperty("#text", out var t) ? t.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        image = text;
                        break;
                    }
                }
            }

            return new ArtistInfoResult
            {
                Artist = artistNode.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? artist : artist,
                Bio = bio,
                ImageUrl = image,
                Source = "Last.fm",
            };
        }
        catch
        {
            return new ArtistInfoResult { Artist = artist, Source = "Local library" };
        }
    }

    private static string? StripTags(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;
        try
        {
            return XElement.Parse($"<r>{html}</r>").Value.Trim();
        }
        catch
        {
            return html.Replace("<br/>", Environment.NewLine, StringComparison.OrdinalIgnoreCase).Trim();
        }
    }
}
