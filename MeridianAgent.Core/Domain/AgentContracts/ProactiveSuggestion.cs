namespace MeridianAgent.Domain.AgentContracts;

/// <summary>One unprompted idea the agent can surface in the traveler's chat.</summary>
public sealed record ProactiveSuggestion(
    string Title,
    string Rationale,
    double ConfidenceScore,
    string? SuggestedTiming = null,
    string? VenueType = null,
    string? Interest = null);

/// <summary>All the inputs a proactive suggestion run needs (no user query — the agent speaks first).</summary>
public sealed record ProactiveRequest(
    UserProfile Profile,
    TravelContext Context,
    IReadOnlyList<NearbyVenue> NearbyVenues,
    DestinationRulebook Rulebook);
