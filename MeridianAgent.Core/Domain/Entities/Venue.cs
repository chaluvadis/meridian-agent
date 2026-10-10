using System.Text.Json.Serialization;

namespace MeridianAgent.Domain.Entities;

/// <summary>A venue proposed by the Search Agent and surfaced to the user by the Synthesizer.</summary>
public sealed record Venue
{
    [JsonPropertyName("venue_name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("venue_type")]
    public Domain.VenueType VenueType { get; init; }

    [JsonPropertyName("category")]
    public Domain.VenueCategory Category { get; init; }

    [JsonPropertyName("travel_time_minutes")]
    public int TravelTimeMinutes { get; init; }

    [JsonPropertyName("why_it_fits")]
    public string WhyItFits { get; init; } = string.Empty;

    [JsonPropertyName("estimated_duration")]
    public string EstimatedDuration { get; init; } = string.Empty;

    [JsonPropertyName("best_time_to_visit")]
    public string BestTimeToVisit { get; init; } = string.Empty;
}
