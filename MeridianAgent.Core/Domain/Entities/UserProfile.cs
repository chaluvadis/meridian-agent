using System.Text.Json.Serialization;

namespace MeridianAgent.Domain.Entities;

/// <summary>Traveler preferences the Context and Safeguard agents read from.</summary>
public sealed record UserProfile
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "default";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = "Traveler";

    [JsonPropertyName("destination")]
    public string Destination { get; init; } = string.Empty;

    [JsonPropertyName("traveler_profile")]
    public Domain.TravelerProfile TravelerProfile { get; init; } = Domain.TravelerProfile.FirstTime;

    [JsonPropertyName("group_composition")]
    public Domain.GroupComposition GroupComposition { get; init; } = Domain.GroupComposition.Solo;

    [JsonPropertyName("dietary_rules")]
    public IReadOnlyList<string> DietaryRules { get; init; } = Array.Empty<string>();

    [JsonPropertyName("interests")]
    public IReadOnlyList<string> Interests { get; init; } = Array.Empty<string>();

    [JsonPropertyName("avoid_list")]
    public IReadOnlyList<string> AvoidList { get; init; } = Array.Empty<string>();

    [JsonPropertyName("pacing")]
    public Domain.PacingPreference Pacing { get; init; } = Domain.PacingPreference.Moderate;

    [JsonPropertyName("budget")]
    public Domain.BudgetRange Budget { get; init; } = Domain.BudgetRange.Medium;

    [JsonPropertyName("attire_note")]
    public string? AttireNote { get; init; }

    [JsonPropertyName("home_base")]
    public string? HomeBase { get; init; }

    public static UserProfile Default => new();
}
