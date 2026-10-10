using System.Text.Json.Serialization;
using MeridianAgent.Domain;

namespace MeridianAgent.Domain.AgentContracts;

/// <summary>
/// Output of the Context Agent: the subset of profile constraints that matter for this query right now.
/// Passed between agents as an immutable record.
/// </summary>
public sealed record ExtractedConstraints
{
    [JsonPropertyName("query_intent")]
    public QueryIntent QueryIntent { get; init; }

    [JsonPropertyName("relevant_dietary")]
    public IReadOnlyList<string>? RelevantDietary { get; init; }

    [JsonPropertyName("relevant_interests")]
    public IReadOnlyList<string>? RelevantInterests { get; init; }

    [JsonPropertyName("avoid_list")]
    public IReadOnlyList<string>? AvoidList { get; init; }

    [JsonPropertyName("pacing_preference")]
    public PacingPreference PacingPreference { get; init; } = PacingPreference.Moderate;

    [JsonPropertyName("energy_level")]
    public EnergyLevel EnergyLevel { get; init; } = EnergyLevel.Medium;

    [JsonPropertyName("time_sensitivity")]
    public TimeSensitivity TimeSensitivity { get; init; } = TimeSensitivity.Normal;

    [JsonPropertyName("budget_range")]
    public BudgetRange? BudgetRange { get; init; }
}
