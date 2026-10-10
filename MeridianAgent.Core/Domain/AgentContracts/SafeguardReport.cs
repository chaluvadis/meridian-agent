using System.Text.Json.Serialization;
using MeridianAgent.Domain;
using MeridianAgent.Domain.AgentContracts;

namespace MeridianAgent.Domain.AgentContracts;

/// <summary>One risk the Safeguard Agent flagged, with a non-judgmental way to avoid it.</summary>
public sealed record RiskWarning
{
    [JsonPropertyName("applies_to_venue")]
    public string AppliesToVenue { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public RiskSeverity Severity { get; init; } = RiskSeverity.Low;

    [JsonPropertyName("rule_category")]
    public RuleCategory RuleCategory { get; init; } = RuleCategory.Other;

    [JsonPropertyName("risk_description")]
    public string RiskDescription { get; init; } = string.Empty;

    [JsonPropertyName("gentle_fix")]
    public string GentleFix { get; init; } = string.Empty;
}

/// <summary>Output of the Safeguard Agent: cleared, or flagged with warnings.</summary>
public sealed record SafeguardReport(SafeguardStatus Status, IReadOnlyList<RiskWarning> Warnings)
{
    public static readonly SafeguardReport Cleared = new(SafeguardStatus.Cleared, Array.Empty<RiskWarning>());

    public bool IsFlagged => Status == SafeguardStatus.Flagged && Warnings.Count > 0;
}
