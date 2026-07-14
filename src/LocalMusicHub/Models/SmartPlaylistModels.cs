using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalMusicHub.Models;

public sealed class SmartPlaylistRules
{
    /// <summary>"all" = every rule must match (AND). "any" = at least one rule (OR).</summary>
    public string MatchMode { get; set; } = "all";

    public List<SmartPlaylistRule> Rules { get; set; } = [];

    public static SmartPlaylistRules HighlyRated { get; } = new()
    {
        Rules = [new SmartPlaylistRule { Field = "rating", Operator = "min", Value = "4" }],
    };

    public static SmartPlaylistRules AddedLast30Days { get; } = new()
    {
        Rules = [new SmartPlaylistRule { Field = "date_added", Operator = "last_days", Value = "30" }],
    };

    public static SmartPlaylistRules NeverPlayed { get; } = new()
    {
        Rules = [new SmartPlaylistRule { Field = "never_played", Operator = "is_true", Value = "" }],
    };

    public static SmartPlaylistRules RecentlyPlayed { get; } = new()
    {
        Rules = [new SmartPlaylistRule { Field = "last_played", Operator = "last_days", Value = "14" }],
    };

    public static SmartPlaylistRules? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SmartPlaylistRules();

        try
        {
            return JsonSerializer.Deserialize<SmartPlaylistRules>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed class SmartPlaylistRule
{
    public string Field { get; init; } = "";
    public string Operator { get; init; } = "";
    public string Value { get; init; } = "";
}
