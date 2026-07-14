using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace LocalMusicHub.Services;

public sealed record UpdateCheckResult(
    bool Success,
    string? LatestVersion,
    string? ReleasePageUrl,
    string? ErrorMessage)
{
    public bool IsNewerThanCurrent { get; init; }
    public bool NoPublishedReleases { get; init; }
    public string? InstallerDownloadUrl { get; init; }
}

public static class UpdateCheckService
{
  public const string GitHubRepo = "Litbolt123/Local-Music-Hub";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "LocalMusicHub-UpdateCheck/1.0");
        c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        return c;
    }

    public static string ReleasesPageUrl => $"https://github.com/{GitHubRepo}/releases";

    public static Version CurrentAssemblyVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static async Task<UpdateCheckResult> CheckLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        const int perPage = 100;
        try
        {
            Version? best = null;
            string? installerUrl = null;

            for (var page = 1; page <= 5; page++)
            {
                var url = $"https://api.github.com/repos/{GitHubRepo}/releases?per_page={perPage}&page={page}";
                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return new UpdateCheckResult(false, null, null, "GitHub returned 404 — check repository name or network.");

                if (!resp.IsSuccessStatusCode)
                    return new UpdateCheckResult(false, null, null, $"GitHub returned {(int)resp.StatusCode} ({resp.ReasonPhrase}).");

                var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var arr = doc.RootElement;
                if (arr.ValueKind != JsonValueKind.Array)
                    return new UpdateCheckResult(false, null, null, "Unexpected GitHub releases response.");

                if (arr.GetArrayLength() == 0)
                    break;

                foreach (var rel in arr.EnumerateArray())
                {
                    if (rel.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True && draft.GetBoolean())
                        continue;

                    if (!rel.TryGetProperty("tag_name", out var tagEl))
                        continue;
                    var tag = tagEl.GetString()?.Trim() ?? "";
                    if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                        tag = tag[1..];
                    if (!Version.TryParse(tag, out var v))
                        continue;

                    if (best is null || v > best)
                    {
                        best = v;
                        installerUrl = FindSetupInstallerUrl(rel);
                    }
                }

                if (arr.GetArrayLength() < perPage)
                    break;
            }

            if (best is null)
            {
                return new UpdateCheckResult(true, null, ReleasesPageUrl, null)
                {
                    NoPublishedReleases = true,
                    IsNewerThanCurrent = false,
                };
            }

            var newer = best > CurrentAssemblyVersion;
            return new UpdateCheckResult(true, best.ToString(3), ReleasesPageUrl, null)
            {
                IsNewerThanCurrent = newer,
                InstallerDownloadUrl = installerUrl,
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, null, null, ex.Message);
        }
    }

    private static string? FindSetupInstallerUrl(JsonElement releaseRoot)
    {
        if (!releaseRoot.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameEl))
                continue;
            var name = nameEl.GetString() ?? "";
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!name.StartsWith("LocalMusicHub-Setup-", StringComparison.OrdinalIgnoreCase))
                continue;
            if (asset.TryGetProperty("browser_download_url", out var urlEl))
            {
                var u = urlEl.GetString();
                if (!string.IsNullOrEmpty(u))
                    return u;
            }
        }

        return null;
    }

    public static void OpenUpdateDownload(string? installerDownloadUrl)
    {
        var url = !string.IsNullOrWhiteSpace(installerDownloadUrl) ? installerDownloadUrl : ReleasesPageUrl;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            Process.Start(new ProcessStartInfo { FileName = ReleasesPageUrl, UseShellExecute = true });
        }
    }

    public static async Task<(string? FilePath, string? Error)> DownloadInstallerToTempAsync(
        string browserDownloadUrl,
        string? versionDisplay,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(browserDownloadUrl))
            return (null, "No download URL.");

        var label = string.IsNullOrWhiteSpace(versionDisplay)
            ? "latest"
            : string.Join("-", versionDisplay.Trim().Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (label.Length > 48)
            label = label[..48];

        var path = Path.Combine(Path.GetTempPath(), $"LocalMusicHub-Setup-{label}.exe");

        using var dl = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        dl.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "LocalMusicHub-InstallerDownload/1.0");
        dl.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/octet-stream");

        try
        {
            using var resp = await dl.GetAsync(browserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return (null, $"Download failed ({(int)resp.StatusCode} {resp.ReasonPhrase}).");

            await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
            await resp.Content.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(path) || new FileInfo(path).Length < 512 * 1024)
            {
                try { File.Delete(path); } catch { /* ignore */ }
                return (null, "Downloaded file was too small — try the Releases page in your browser.");
            }

            return (path, null);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
            return (null, ex.Message);
        }
    }
}
