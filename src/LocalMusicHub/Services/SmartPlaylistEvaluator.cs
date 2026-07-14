using LocalMusicHub.Models;
using Microsoft.Data.Sqlite;

namespace LocalMusicHub.Services;

public static class SmartPlaylistEvaluator
{
    public static string BuildWhereClause(SmartPlaylistRules rules, SqliteCommand cmd)
    {
        var clauses = new List<string>();
        for (var i = 0; i < rules.Rules.Count; i++)
        {
            var clause = BuildClause(rules.Rules[i], cmd, i);
            if (!string.IsNullOrEmpty(clause))
                clauses.Add(clause);
        }

        if (clauses.Count == 0)
            return "1=1";

        var joiner = string.Equals(rules.MatchMode, "any", StringComparison.OrdinalIgnoreCase)
            ? " OR "
            : " AND ";
        return clauses.Count == 1 ? clauses[0] : "(" + string.Join(joiner, clauses) + ")";
    }

    private static string? BuildClause(SmartPlaylistRule rule, SqliteCommand cmd, int index)
    {
        var field = rule.Field.ToLowerInvariant();
        var op = rule.Operator.ToLowerInvariant();
        var value = rule.Value?.Trim() ?? "";

        if (field == "rating" && op == "min" && int.TryParse(value, out var minRating))
            return AddParam(cmd, $"$r{index}", Math.Clamp(minRating, 0, 5), $"rating >= $r{index}");

        if (field == "rating" && op == "max" && int.TryParse(value, out var maxRating))
            return AddParam(cmd, $"$rmax{index}", Math.Clamp(maxRating, 0, 5), $"rating <= $rmax{index}");

        if (field == "rating" && (op is "equals" or "is") && int.TryParse(value, out var exactRating))
            return AddParam(cmd, $"$req{index}", Math.Clamp(exactRating, 0, 5), $"rating = $req{index}");

        if (field == "genre" && !string.IsNullOrWhiteSpace(value) && op is "contains" or "equals" or "is")
        {
            return op == "contains"
                ? AddLikeParam(cmd, $"$g{index}", value,
                    $"LOWER(TRIM(genre)) LIKE '%' || LOWER($g{index}) || '%'")
                : AddLikeParam(cmd, $"$geq{index}", value, GenreNormalizer.SqlSegmentEquals($"$geq{index}"));
        }

        if (field == "artist" && !string.IsNullOrWhiteSpace(value))
        {
            // "Is" still matches featured-artist strings via contains, plus exact artist/album_artist.
            if (op is "contains" or "equals" or "is")
            {
                var name = $"$a{index}";
                return AddLikeParam(cmd, name, value, $"""
                    (
                        LOWER(TRIM(COALESCE(artist, ''))) = LOWER({name})
                        OR LOWER(TRIM(COALESCE(album_artist, ''))) = LOWER({name})
                        OR LOWER(COALESCE(artist, '')) LIKE '%' || LOWER({name}) || '%'
                        OR LOWER(COALESCE(album_artist, '')) LIKE '%' || LOWER({name}) || '%'
                    )
                    """);
            }
        }

        if (field == "album" && !string.IsNullOrWhiteSpace(value))
        {
            if (op is "contains" or "equals" or "is")
            {
                var isUnknown = value.Equals("Unknown Album", StringComparison.OrdinalIgnoreCase);
                if (isUnknown && op is "equals" or "is")
                {
                    return """
                        (
                            TRIM(COALESCE(album, '')) = ''
                            OR LOWER(TRIM(album)) = 'unknown album'
                        )
                        """;
                }

                return op == "contains"
                    ? AddLikeParam(cmd, $"$al{index}", value,
                        $"LOWER(TRIM(COALESCE(NULLIF(album, ''), 'Unknown Album'))) LIKE '%' || LOWER($al{index}) || '%'")
                    : AddLikeParam(cmd, $"$aleq{index}", value,
                        $"LOWER(TRIM(COALESCE(NULLIF(album, ''), 'Unknown Album'))) = LOWER($aleq{index})");
            }
        }

        if (field == "title" && !string.IsNullOrWhiteSpace(value) && op is "contains" or "equals" or "is")
        {
            return op == "contains"
                ? AddLikeParam(cmd, $"$t{index}", value,
                    $"LOWER(TRIM(title)) LIKE '%' || LOWER($t{index}) || '%'")
                : AddLikeParam(cmd, $"$teq{index}", value,
                    $"LOWER(TRIM(title)) = LOWER($teq{index})");
        }

        if (field == "date_added" && op == "last_days" && int.TryParse(value, out var days) && days > 0)
            return AddParam(cmd, $"$d{index}", DateTime.UtcNow.AddDays(-days).ToString("O"),
                $"date_added_utc >= $d{index}");

        if (field == "last_played" && op == "last_days" && int.TryParse(value, out var playedDays) && playedDays > 0)
            return AddParam(cmd, $"$lp{index}", DateTime.UtcNow.AddDays(-playedDays).ToString("O"),
                $"(last_played_utc IS NOT NULL AND last_played_utc >= $lp{index})");

        if (field == "play_count" && op == "min" && int.TryParse(value, out var minPlays))
            return AddParam(cmd, $"$pcmin{index}", Math.Max(0, minPlays), $"play_count >= $pcmin{index}");

        if (field == "play_count" && op == "max" && int.TryParse(value, out var maxPlays))
            return AddParam(cmd, $"$pcmax{index}", Math.Max(0, maxPlays), $"play_count <= $pcmax{index}");

        if (field == "play_count" && (op is "equals" or "is") && int.TryParse(value, out var exactPlays))
            return AddParam(cmd, $"$pceq{index}", Math.Max(0, exactPlays), $"play_count = $pceq{index}");

        if (field == "year" && op == "min" && int.TryParse(value, out var minYear))
            return AddParam(cmd, $"$ymin{index}", minYear, $"(year IS NOT NULL AND year >= $ymin{index})");

        if (field == "year" && op == "max" && int.TryParse(value, out var maxYear))
            return AddParam(cmd, $"$ymax{index}", maxYear, $"(year IS NOT NULL AND year <= $ymax{index})");

        if (field == "year" && (op is "equals" or "is") && int.TryParse(value, out var exactYear))
            return AddParam(cmd, $"$yeq{index}", exactYear, $"year = $yeq{index}");

        if (field == "format" && !string.IsNullOrWhiteSpace(value) && op is "equals" or "is" or "contains")
        {
            var fmt = value.TrimStart('.');
            return AddLikeParam(cmd, $"$fmt{index}", fmt,
                $"LOWER(TRIM(format)) LIKE '%' || LOWER($fmt{index}) || '%'");
        }

        if (field == "never_played" && op == "is_true")
            return "play_count = 0";

        if (field == "unrated" && op == "is_true")
            return "rating = 0";

        if (field == "bitrate" && op == "min" && int.TryParse(value, out var minBitrate))
            return AddParam(cmd, $"$brmin{index}", Math.Max(0, minBitrate), $"bitrate >= $brmin{index}");

        if (field == "bitrate" && op == "max" && int.TryParse(value, out var maxBitrate))
            return AddParam(cmd, $"$brmax{index}", Math.Max(0, maxBitrate), $"bitrate <= $brmax{index}");

        if (field == "duration" && op == "min" && double.TryParse(value, out var minSec))
            return AddParam(cmd, $"$dmin{index}", (long)(Math.Max(0, minSec) * 1000),
                $"duration_ms >= $dmin{index}");

        if (field == "duration" && op == "max" && double.TryParse(value, out var maxSec))
            return AddParam(cmd, $"$dmax{index}", (long)(Math.Max(0, maxSec) * 1000),
                $"duration_ms <= $dmax{index}");

        return null;
    }

    private static string AddParam(SqliteCommand cmd, string name, object value, string clause)
    {
        cmd.Parameters.AddWithValue(name, value);
        return clause;
    }

    private static string AddLikeParam(SqliteCommand cmd, string name, string value, string clause)
    {
        cmd.Parameters.AddWithValue(name, value);
        return clause;
    }
}
