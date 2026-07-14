using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalMusicHub.Services;

public sealed class DownloaderHistoryEntry
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public string Format { get; set; } = "";
    public string ContentKind { get; set; } = "";
    public string Scope { get; set; } = "";
    public string CompletedUtc { get; set; } = "";
    public bool Success { get; set; }
}

public static class DownloaderHistoryReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static DownloaderHistoryEntry? FindSuccessfulByUrl(string url, DateTime queuedAfterUtc)
    {
        foreach (var entry in ReadEntries())
        {
            if (!entry.Success || !YouTubeUrlHelper.UrlsMatch(entry.Url, url))
                continue;

            if (!TryParseUtc(entry.CompletedUtc, out var completed))
                continue;

            if (completed >= queuedAfterUtc.AddSeconds(-5))
                return entry;
        }

        return null;
    }

    public static IReadOnlyList<DownloaderHistoryEntry> ReadEntries()
    {
        try
        {
            if (!File.Exists(AppPaths.YouTubeDownloaderHistoryPath))
                return [];

            var json = File.ReadAllText(AppPaths.YouTubeDownloaderHistoryPath);
            return JsonSerializer.Deserialize<List<DownloaderHistoryEntry>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static bool TryParseUtc(string value, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out utc);
    }
}
