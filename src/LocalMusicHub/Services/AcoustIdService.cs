using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using LocalMusicHub.Data;
using LocalMusicHub.Models;

namespace LocalMusicHub.Services;

public sealed class AcoustIdLookupResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string? RecordingId { get; init; }
}

public sealed class AcoustIdService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(App.Settings.AcoustIdApiKey) &&
        !string.IsNullOrWhiteSpace(ResolveFpcalcPath());

    public async Task<AcoustIdLookupResult> IdentifyTrackAsync(
        LibraryRepository repository,
        LibraryTrack track,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return new AcoustIdLookupResult { Message = "Configure AcoustID API key and fpcalc path in Settings." };

        var audioPath = track.AudioFilePath;
        if (!File.Exists(audioPath))
            return new AcoustIdLookupResult { Message = "Audio file not found." };

        var fingerprint = await GenerateFingerprintAsync(audioPath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(fingerprint.Duration) || string.IsNullOrWhiteSpace(fingerprint.Fingerprint))
            return new AcoustIdLookupResult { Message = "Could not fingerprint this file." };

        var lookup = await LookupAcoustIdAsync(fingerprint.Duration, fingerprint.Fingerprint, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.RecordingId is null)
            return new AcoustIdLookupResult { Message = lookup.Message ?? "No AcoustID match found." };

        var applied = await MusicBrainzService.ApplyRecordingToTrackAsync(repository, track, lookup.RecordingId, cancellationToken)
            .ConfigureAwait(false);
        return applied
            ? new AcoustIdLookupResult { Success = true, Message = "Tags updated from MusicBrainz.", RecordingId = lookup.RecordingId }
            : new AcoustIdLookupResult { Message = "Matched AcoustID but could not apply MusicBrainz tags." };
    }

    private static string? ResolveFpcalcPath()
    {
        var configured = App.Settings.FpcalcPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var local = Path.Combine(AppContext.BaseDirectory, "fpcalc.exe");
        return File.Exists(local) ? local : null;
    }

    private static async Task<(string? Duration, string? Fingerprint)> GenerateFingerprintAsync(
        string audioPath,
        CancellationToken cancellationToken)
    {
        var fpcalc = ResolveFpcalcPath();
        if (fpcalc is null)
            return (null, null);

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fpcalc,
                    Arguments = $"-json \"{audioPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
                return (null, null);

            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            var duration = root.TryGetProperty("duration", out var d) ? d.GetRawText() : null;
            var fingerprint = root.TryGetProperty("fingerprint", out var f) ? f.GetString() : null;
            return (duration, fingerprint);
        }
        catch
        {
            return (null, null);
        }
    }

    private async Task<(string? RecordingId, string? Message)> LookupAcoustIdAsync(
        string duration,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = "https://api.acoustid.org/v2/lookup";
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client"] = App.Settings.AcoustIdApiKey!,
                ["duration"] = duration,
                ["fingerprint"] = fingerprint,
                ["meta"] = "recordings",
            });
            using var response = await Http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                return (null, "No AcoustID match.");

            foreach (var result in results.EnumerateArray())
            {
                if (!result.TryGetProperty("recordings", out var recordings))
                    continue;
                foreach (var recording in recordings.EnumerateArray())
                {
                    if (recording.TryGetProperty("id", out var idNode))
                        return (idNode.GetString(), null);
                }
            }

            return (null, "No linked MusicBrainz recording.");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}
