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

    /// <summary>
    /// Which preference the Search Agent bent to offer this venue, phrased for the traveler
    /// ("It's 15 minutes away instead of your usual 10"), or null when the match was exact. Present
    /// when the agent negotiated rather than refused, so the Synthesizer can say so plainly.
    /// </summary>
    [JsonPropertyName("compromise")]
    public string? Compromise { get; init; }

    [JsonPropertyName("estimated_duration")]
    public string EstimatedDuration { get; init; } = string.Empty;

    [JsonPropertyName("best_time_to_visit")]
    public string BestTimeToVisit { get; init; } = string.Empty;
}
