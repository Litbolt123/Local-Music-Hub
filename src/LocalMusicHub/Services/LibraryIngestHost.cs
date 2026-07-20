using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalMusicHub.Services;

/// <summary>
/// Local HTTP endpoint for YouTube Downloader to push completed music downloads into the library.
/// </summary>
public sealed class LibraryIngestHost : IDisposable
{
    public const int DefaultPort = 47385;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private string _token = "";

    public event EventHandler<LibraryIngestEvent>? IngestRequested;

    public bool IsRunning => _listener?.IsListening == true;
    public int Port { get; private set; }

    public void ApplySettings(AppSettings settings)
    {
        Stop();

        if (!settings.IntegrateYouTubeDownloader || !settings.LibraryIngestEnabled)
            return;

        AppSettingsService.EnsureLibraryIngestToken(settings);
        _token = settings.LibraryIngestToken ?? "";
        Port = settings.LibraryIngestPort is > 0 and < 65536
            ? settings.LibraryIngestPort
            : DefaultPort;

        Start();
    }

    private void Start()
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();

        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is { IsListening: true })
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                /* keep listening */
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;
            AddCorsHeaders(response);

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            var path = request.Url?.AbsolutePath?.TrimEnd('/') ?? "";

            if (path == "/health" && request.HttpMethod == "GET")
            {
                await WriteJsonAsync(response, 200, new
                {
                    status = "ok",
                    app = "Local Music Hub",
                    ingest = true,
                    version = App.VersionDisplay,
                }).ConfigureAwait(false);
                return;
            }

            if (path == "/library/ingest" && request.HttpMethod == "POST")
            {
                if (!ValidateToken(request))
                {
                    await WriteJsonAsync(response, 401, new { error = "unauthorized" }).ConfigureAwait(false);
                    return;
                }

                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                var payload = JsonSerializer.Deserialize<LibraryIngestPayload>(body, JsonOptions);
                if (payload is null || string.IsNullOrWhiteSpace(payload.Path))
                {
                    await WriteJsonAsync(response, 400, new { error = "path required" }).ConfigureAwait(false);
                    return;
                }

                if (!string.Equals(payload.ContentKind, "music", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(payload.ContentKind, "auto", StringComparison.OrdinalIgnoreCase)
                    && payload.ContentKind is not null)
                {
                    await WriteJsonAsync(response, 202, new { accepted = false, reason = "not music" }).ConfigureAwait(false);
                    return;
                }

                IngestRequested?.Invoke(this, new LibraryIngestEvent
                {
                    Path = payload.Path.Trim(),
                    ContentKind = payload.ContentKind ?? "music",
                    SourceUrl = payload.SourceUrl,
                    CompletedUtc = payload.CompletedUtc,
                    ImportFolder = payload.ImportFolder,
                });

                await WriteJsonAsync(response, 200, new { accepted = true }).ConfigureAwait(false);
                return;
            }

            await WriteJsonAsync(response, 404, new { error = "not found" }).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private bool ValidateToken(HttpListenerRequest request)
    {
        if (string.IsNullOrEmpty(_token))
            return false;

        var header = request.Headers["X-Hub-Token"] ?? request.Headers["X-Extension-Token"];
        return string.Equals(header, _token, StringComparison.Ordinal);
    }

    private static void AddCorsHeaders(HttpListenerResponse response)
    {
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-Hub-Token, X-Extension-Token");
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }

        if (_listener is not null)
        {
            try { _listener.Stop(); } catch { /* ignore */ }
            try { _listener.Close(); } catch { /* ignore */ }
            _listener = null;
        }

        _cts?.Dispose();
        _cts = null;
        _listenTask = null;
    }

    public void Dispose() => Stop();

    private sealed class LibraryIngestPayload
    {
        public string Path { get; set; } = "";
        public string? ContentKind { get; set; }
        public string? SourceUrl { get; set; }
        public string? CompletedUtc { get; set; }
        public bool ImportFolder { get; set; }
    }
}
