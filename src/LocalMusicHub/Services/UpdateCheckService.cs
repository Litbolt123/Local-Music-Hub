using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalMusicHub.Services;

/// <summary>
/// Best-effort check against GitHub Releases (no silent in-app upgrade — user runs the published installer).
/// Mirrors What Am I Doing: paginated max version, Setup EXE asset, download-to-temp.
/// </summary>
public static class UpdateCheckService
{
    /// <summary>Owner/repo for the public GitHub project (releases API).</summary>
    public const string GitHubRepo = "Litbolt123/Local-Music-Hub";

    private const string SetupPrefix = "LocalMusicHub-Setup-";

    private static readonly HttpClient Http = CreateClient();

    private static readonly Regex SetupVersionRegex = new(
        @"^LocalMusicHub-Setup-(?<ver>\d+(?:\.\d+){1,3})(?:[-_].*)?\.exe$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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

    /// <summary>
    /// Compares the installed build to the highest published non-draft release we can parse (paginated list).
    /// Prefers the version embedded in <c>LocalMusicHub-Setup-*.exe</c> when the tag and asset disagree
    /// (e.g. tag v0.13.0 that only attached Setup-0.11.6.exe).
    /// </summary>
    public static async Task<UpdateCheckResult> CheckLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        const int perPage = 100;
        try
        {
            Version? best = null;
            string? installerUrl = null;
            var sawAnyPage = false;

            for (var page = 1; page <= 5; page++)
            {
                var url = $"https://api.github.com/repos/{GitHubRepo}/releases?per_page={perPage}&page={page}";
                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new UpdateCheckResult(false, null, null,
                        "GitHub returned 404 — check repository name or network.");
                }

                if (!resp.IsSuccessStatusCode)
                {
                    return new UpdateCheckResult(false, null, null,
                        $"GitHub returned {(int)resp.StatusCode} ({resp.ReasonPhrase}).");
                }

                var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var arr = doc.RootElement;
                if (arr.ValueKind != JsonValueKind.Array)
                    return new UpdateCheckResult(false, null, null, "Unexpected GitHub releases response.");

                sawAnyPage = true;
                if (arr.GetArrayLength() == 0)
                    break;

                foreach (var rel in arr.EnumerateArray())
                {
                    if (rel.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True &&
                        draft.GetBoolean())
                        continue;

                    if (!rel.TryGetProperty("tag_name", out var tagEl))
                        continue;
                    var tag = tagEl.GetString()?.Trim() ?? "";
                    if (tag.StartsWith('v') || tag.StartsWith('V'))
                        tag = tag[1..];
                    if (!Version.TryParse(tag, out var tagVersion))
                        continue;

                    var (assetUrl, assetVersion) = FindSetupInstaller(rel, tagVersion);
                    // What you actually get is the Setup EXE version when present; otherwise the tag.
                    var effective = assetVersion ?? tagVersion;

                    if (best is null || effective > best)
                    {
                        best = effective;
                        installerUrl = assetUrl;
                    }
                }

                if (arr.GetArrayLength() < perPage)
                    break;
            }

            if (!sawAnyPage || best is null)
            {
                return new UpdateCheckResult(true, null, ReleasesPageUrl, null)
                {
                    NoPublishedReleases = true,
                    IsNewerThanCurrent = false,
                };
            }

            var cur = CurrentAssemblyVersion;
            var newer = best > cur;
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

    /// <summary>
    /// Picks <c>LocalMusicHub-Setup-*.exe</c>. Prefers an asset whose filename version matches the release tag;
    /// otherwise the highest parseable Setup version on that release.
    /// </summary>
    private static (string? Url, Version? AssetVersion) FindSetupInstaller(JsonElement releaseRoot, Version tagVersion)
    {
        if (!releaseRoot.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return (null, null);

        string? tagMatchUrl = null;
        string? bestUrl = null;
        Version? bestVer = null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameEl))
                continue;
            var name = nameEl.GetString() ?? "";
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!name.StartsWith(SetupPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!asset.TryGetProperty("browser_download_url", out var urlEl))
                continue;
            var u = urlEl.GetString();
            if (string.IsNullOrEmpty(u))
                continue;

            var assetVer = TryParseSetupFileVersion(name);
            if (assetVer is not null && assetVer == tagVersion)
                tagMatchUrl = u;

            if (assetVer is null)
            {
                bestUrl ??= u;
                continue;
            }

            if (bestVer is null || assetVer > bestVer)
            {
                bestVer = assetVer;
                bestUrl = u;
            }
        }

        if (tagMatchUrl is not null)
            return (tagMatchUrl, tagVersion);

        return (bestUrl, bestVer);
    }

    private static Version? TryParseSetupFileVersion(string fileName)
    {
        var m = SetupVersionRegex.Match(fileName);
        if (!m.Success)
            return null;
        return Version.TryParse(m.Groups["ver"].Value, out var v) ? v : null;
    }

    public static void OpenUpdateDownload(string? installerDownloadUrl)
    {
        var url = !string.IsNullOrWhiteSpace(installerDownloadUrl)
            ? installerDownloadUrl
            : ReleasesPageUrl;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            OpenReleasesInBrowser();
        }
    }

    public static void OpenReleasesInBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ReleasesPageUrl,
                UseShellExecute = true,
            });
        }
        catch
        {
            /* ignore */
        }
    }

    /// <summary>
    /// Downloads the published Inno setup from GitHub into the user temp folder.
    /// Uses a separate HTTP client with a long timeout — not the lightweight API client.
    /// </summary>
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

        var path = Path.Combine(Path.GetTempPath(), $"{SetupPrefix}{label}.exe");
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            path = Path.Combine(Path.GetTempPath(), $"{SetupPrefix}{label}-{Guid.NewGuid():N}.exe");
        }

        using var dl = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        dl.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "LocalMusicHub-InstallerDownload/1.0");
        dl.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/octet-stream");

        try
        {
            using var resp = await dl
                .GetAsync(browserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return (null, $"Download failed ({(int)resp.StatusCode} {resp.ReasonPhrase}).");

            await using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536,
                         FileOptions.Asynchronous))
            {
                await resp.Content.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
            }

            if (!File.Exists(path))
                return (null, "Download finished but the file is missing.");

            var len = new FileInfo(path).Length;
            if (len < 512 * 1024)
            {
                try { File.Delete(path); } catch { /* ignore */ }
                return (null, "Downloaded file was too small — try the Releases page in your browser.");
            }

            return (path, null);
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                /* ignore */
            }

            return (null, ex.Message);
        }
    }
}

/// <summary>Latest GitHub update seen this session (startup card + tray).</summary>
public static class UpdateAvailabilityCache
{
    public static string? PendingVersion { get; private set; }
    public static string? InstallerDownloadUrl { get; private set; }

    public static bool HasPending => !string.IsNullOrWhiteSpace(PendingVersion);

    public static void Set(string? version, string? installerDownloadUrl)
    {
        PendingVersion = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        InstallerDownloadUrl = installerDownloadUrl;
    }

    public static void Clear()
    {
        PendingVersion = null;
        InstallerDownloadUrl = null;
    }

    public static UpdateCheckResult ToResult() =>
        new(true, PendingVersion, UpdateCheckService.ReleasesPageUrl, null)
        {
            IsNewerThanCurrent = true,
            InstallerDownloadUrl = InstallerDownloadUrl,
        };
}

public sealed record UpdateCheckResult(
    bool Success,
    string? LatestVersion,
    string? ReleasePageUrl,
    string? ErrorMessage)
{
    public bool IsNewerThanCurrent { get; init; }

    /// <summary>True when no parseable published releases were found.</summary>
    public bool NoPublishedReleases { get; init; }

    /// <summary>Direct <c>browser_download_url</c> for <c>LocalMusicHub-Setup-*.exe</c> when GitHub attached it.</summary>
    public string? InstallerDownloadUrl { get; init; }
}
