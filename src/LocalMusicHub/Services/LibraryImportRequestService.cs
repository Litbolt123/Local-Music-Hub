using System.Text.Json;

namespace LocalMusicHub.Services;

public sealed class LibraryImportRequest
{
    public string Path { get; init; } = "";
    public bool ImportFolder { get; init; }
    public string RequestedUtc { get; init; } = "";
}

public static class LibraryImportRequestService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string ImportRequestPath =>
        Path.Combine(AppPaths.DataDirectory, "import-request.json");

    public static bool TryReadPending(out LibraryImportRequest? request)
    {
        request = null;
        try
        {
            if (!File.Exists(ImportRequestPath))
                return false;

            var json = File.ReadAllText(ImportRequestPath);
            var payload = JsonSerializer.Deserialize<ImportRequestPayload>(json, JsonOptions);
            if (string.IsNullOrWhiteSpace(payload?.Path))
                return false;

            request = new LibraryImportRequest
            {
                Path = payload.Path,
                ImportFolder = payload.ImportFolder,
                RequestedUtc = payload.RequestedUtc ?? "",
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void ClearPending()
    {
        try
        {
            if (File.Exists(ImportRequestPath))
                File.Delete(ImportRequestPath);
        }
        catch
        {
            /* ignore */
        }
    }

    public static void WritePending(string path, bool importFolder)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var payload = new ImportRequestPayload
            {
                Path = Path.GetFullPath(path),
                ImportFolder = importFolder,
                RequestedUtc = DateTime.UtcNow.ToString("o"),
            };
            File.WriteAllText(ImportRequestPath, JsonSerializer.Serialize(payload, JsonOptions));
        }
        catch
        {
            /* ignore */
        }
    }

    private sealed class ImportRequestPayload
    {
        public string Path { get; set; } = "";
        public bool ImportFolder { get; set; }
        public string? RequestedUtc { get; set; }
    }
}
