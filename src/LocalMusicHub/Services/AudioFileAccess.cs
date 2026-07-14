namespace LocalMusicHub.Services;

using LocalMusicHub.Models;

public static class AudioFileAccess
{
    public static bool IsSharingViolation(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is not IOException io)
                continue;

            if (io.HResult == unchecked((int)0x80070020))
                return true;

            var message = io.Message;
            if (message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("sharing violation", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("used by another process", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<bool> WaitUntilReadableAsync(
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(path))
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);
                return stream.Length > 0;
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                /* yt-dlp or tag embed still writing */
            }
            catch (UnauthorizedAccessException)
            {
                /* transient while file is created */
            }

            await Task.Delay(750, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    public static async Task<LibraryTrack?> ReadTrackWhenReadyAsync(
        string path,
        DateTime dateAddedUtc,
        CancellationToken cancellationToken = default)
    {
        if (!AudioTagReader.IsSupported(path))
            return null;

        if (!await WaitUntilReadableAsync(path, TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false))
            return null;

        const int maxAttempts = 10;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return AudioTagReader.Read(path, dateAddedUtc);
            }
            catch (Exception ex) when (IsSharingViolation(ex) && attempt < maxAttempts - 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400 * (attempt + 1)), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return null;
    }
}
