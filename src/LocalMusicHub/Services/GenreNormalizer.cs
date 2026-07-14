namespace LocalMusicHub.Services;

public static class GenreNormalizer
{
    private static readonly char[] Separators = [';', '/', '|', ','];

    public static IEnumerable<string> SplitGenres(string? genre)
    {
        if (string.IsNullOrWhiteSpace(genre))
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in genre.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment.Length == 0)
                continue;
            if (seen.Add(segment))
                yield return segment;
        }
    }

    /// <summary>Deduped genres joined for storage (e.g. "Pop; Rock").</summary>
    public static string NormalizeStored(string? genre) =>
        string.Join("; ", SplitGenres(genre).ToList());

    public static bool ContainsGenre(string? trackGenre, string genre)
    {
        if (string.IsNullOrWhiteSpace(genre))
            return false;

        var needle = genre.Trim();
        return SplitGenres(trackGenre).Any(g =>
            string.Equals(g, needle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>SQL: track genre field contains a segment equal to the bound parameter.</summary>
    public static string SqlSegmentEquals(string genreParam) =>
        $"""
        (
            LOWER(TRIM(genre)) = LOWER({genreParam})
            OR (';' || LOWER(REPLACE(REPLACE(REPLACE(REPLACE(genre, ' / ', ';'), '/', ';'), '|', ';'), ',', ';')) || ';')
               LIKE '%;' || LOWER(TRIM({genreParam})) || ';%'
        )
        """;
}
