using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalMusicHub.Services;

public sealed class YouTubeDownloaderApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<DownloaderHealthResult> HealthAsync(int port, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(BaseUrl(port) + "/health", cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return DownloaderHealthResult.Fail($"Health check failed ({(int)response.StatusCode}).");

            var health = JsonSerializer.Deserialize<DownloaderHealthResponse>(body, JsonOptions);
            return health?.Status?.Equals("ok", StringComparison.OrdinalIgnoreCase) == true
                ? DownloaderHealthResult.Success(health.App ?? "YouTube Downloader", health.Version ?? "")
                : DownloaderHealthResult.Fail("Downloader API returned an unexpected response.");
        }
        catch (HttpRequestException)
        {
            return DownloaderHealthResult.Fail("YouTube Downloader is not running. Start it and enable the browser extension API.");
        }
        catch (TaskCanceledException)
        {
            return DownloaderHealthResult.Fail("Timed out connecting to YouTube Downloader.");
        }
        catch (Exception ex)
        {
            return DownloaderHealthResult.Fail(ex.Message);
        }
    }

    public async Task<DownloaderCheckResult> CheckAsync(
        int port,
        string token,
        string url,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await PostJsonAsync(port, token, "/check", new { url }, cancellationToken)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return DownloaderCheckResult.Fail("Invalid extension token. Re-open YouTube Downloader settings.");

            if (!response.IsSuccessStatusCode)
                return DownloaderCheckResult.Fail(ParseError(body) ?? $"Check failed ({(int)response.StatusCode}).");

            var check = JsonSerializer.Deserialize<DownloaderCheckResponse>(body, JsonOptions);
            if (check is null)
                return DownloaderCheckResult.Fail("Unexpected check response.");

            return DownloaderCheckResult.FromResponse(check);
        }
        catch (HttpRequestException)
        {
            return DownloaderCheckResult.Fail("YouTube Downloader is not running.");
        }
        catch (TaskCanceledException)
        {
            return DownloaderCheckResult.Fail("Timed out checking URL.");
        }
        catch (Exception ex)
        {
            return DownloaderCheckResult.Fail(ex.Message);
        }
    }

    public async Task<DownloaderDownloadResult> DownloadAsync(
        int port,
        string token,
        string url,
        bool forceRedownload,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                url,
                scope = "single",
                format = "mp3",
                quality = 0,
                contentKind = "music",
                forceRedownload,
            };

            using var response = await PostJsonAsync(port, token, "/download", payload, cancellationToken)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return DownloaderDownloadResult.Fail("Invalid extension token. Re-open YouTube Downloader settings.");

            if (!response.IsSuccessStatusCode)
                return DownloaderDownloadResult.Fail(ParseError(body) ?? $"Download failed ({(int)response.StatusCode}).");

            var result = JsonSerializer.Deserialize<DownloaderDownloadResponse>(body, JsonOptions);
            if (result?.Ok == true)
                return DownloaderDownloadResult.Success(result.Message ?? "Queued");

            return DownloaderDownloadResult.Fail(ParseError(body) ?? "Download was not queued.");
        }
        catch (HttpRequestException)
        {
            return DownloaderDownloadResult.Fail("YouTube Downloader is not running.");
        }
        catch (TaskCanceledException)
        {
            return DownloaderDownloadResult.Fail("Timed out queueing download.");
        }
        catch (Exception ex)
        {
            return DownloaderDownloadResult.Fail(ex.Message);
        }
    }

    private async Task<HttpResponseMessage> PostJsonAsync(
        int port,
        string token,
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl(port) + path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-Extension-Token", token);
        return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static string BaseUrl(int port) => $"http://127.0.0.1:{port}";

    private static string? ParseError(string body)
    {
        try
        {
            var err = JsonSerializer.Deserialize<DownloaderErrorResponse>(body, JsonOptions);
            return string.IsNullOrWhiteSpace(err?.Error) ? null : err.Error;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}

public sealed class DownloaderHealthResult
{
    public bool Ok { get; init; }
    public string? App { get; init; }
    public string? Version { get; init; }
    public string? Error { get; init; }

    public static DownloaderHealthResult Success(string app, string version) =>
        new() { Ok = true, App = app, Version = version };

    public static DownloaderHealthResult Fail(string error) =>
        new() { Ok = false, Error = error };
}

public sealed class DownloaderCheckResult
{
    public bool Ok { get; init; }
    public bool AlreadyDownloaded { get; init; }
    public bool InQueue { get; init; }
    public string? Title { get; init; }
    public string? Path { get; init; }
    public string? Message { get; init; }
    public string? Error { get; init; }

    internal static DownloaderCheckResult FromResponse(DownloaderCheckResponse response) =>
        new()
        {
            Ok = response.Ok,
            AlreadyDownloaded = response.AlreadyDownloaded,
            InQueue = response.InQueue,
            Title = response.Title,
            Path = response.Path,
            Message = response.Message,
        };

    public static DownloaderCheckResult Fail(string error) =>
        new() { Ok = false, Error = error };
}

public sealed class DownloaderDownloadResult
{
    public bool Ok { get; init; }
    public string? Message { get; init; }
    public string? Error { get; init; }

    public static DownloaderDownloadResult Success(string message) =>
        new() { Ok = true, Message = message };

    public static DownloaderDownloadResult Fail(string error) =>
        new() { Ok = false, Error = error };
}

internal sealed class DownloaderHealthResponse
{
    public string? Status { get; set; }
    public string? App { get; set; }
    public string? Version { get; set; }
}

internal sealed class DownloaderCheckResponse
{
    public bool Ok { get; set; }
    public bool AlreadyDownloaded { get; set; }
    public bool InQueue { get; set; }
    public bool InHistory { get; set; }
    public string? Message { get; set; }
    public string? Title { get; set; }
    public string? Path { get; set; }
}

internal sealed class DownloaderDownloadResponse
{
    public bool Ok { get; set; }
    public string? Message { get; set; }
}

internal sealed class DownloaderErrorResponse
{
    public string? Error { get; set; }
}
