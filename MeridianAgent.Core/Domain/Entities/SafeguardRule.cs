using System.Text.Json.Serialization;
using MeridianAgent.Domain;

namespace MeridianAgent.Domain.Entities;

/// <summary>A local law, cultural norm, or venue rule the Safeguard Agent checks the plan against.</summary>
public sealed record SafeguardRule
{
    [JsonPropertyName("rule_id")]
    public string RuleId { get; init; } = Guid.NewGuid().ToString("n");

    [JsonPropertyName("rule_category")]
    public RuleCategory Category { get; init; } = RuleCategory.Other;

    [JsonPropertyName("rule")]
    public string Rule { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public RiskSeverity Severity { get; init; } = RiskSeverity.Low;

    [JsonPropertyName("gentle_fix")]
    public string GentleFix { get; init; } = string.Empty;
}

/// <summary>All known rules for a destination.</summary>
public sealed record DestinationRulebook
{
    [JsonPropertyName("destination")]
    public string Destination { get; init; } = string.Empty;

    [JsonPropertyName("rules")]
    public IReadOnlyList<SafeguardRule> Rules { get; init; } = Array.Empty<SafeguardRule>();

    public static DestinationRulebook Empty => new();
}
