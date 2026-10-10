namespace MeridianAgent.Application.DTOs;

/// <summary>One user turn: everything the pipeline needs to answer.</summary>
public sealed record TravelRequest(
    string Query,
    UserProfile Profile,
    TravelContext Context,
    IReadOnlyList<NearbyVenue> NearbyVenues,
    DestinationRulebook Rulebook)
{
    public static TravelRequest Create(string query, UserProfile? profile = null) => new(
        query,
        profile ?? UserProfile.Default,
        default!,
        Array.Empty<NearbyVenue>(),
        DestinationRulebook.Empty);
}

/// <summary>Everything the Synthesizer needs to speak to the user.</summary>
public sealed record SynthesisRequest(
    string Query,
    ProposedItinerary Itinerary,
    SafeguardReport Report,
    string TravelerName = "Traveler");

/// <summary>
/// A structured Synthesizer response: the message to render plus the one-tap chips that belong
/// under it. Produced by a single JSON-mode call so the traveler can act without typing.
/// </summary>
public sealed record SynthesisResponse(
    string Message,
    IReadOnlyList<QuickAction> QuickActions)
{
    public static readonly SynthesisResponse Empty = new(string.Empty, Array.Empty<QuickAction>());
}

/// <summary>Non-streaming result of a full pipeline run.</summary>
public sealed record TravelResponse(
    string ResponseText,
    ProposedItinerary Itinerary,
    SafeguardReport SafeguardReport,
    IReadOnlyList<QuickAction> QuickActions)
{
    public static readonly TravelResponse Empty = new(
        string.Empty,
        ProposedItinerary.Empty,
        SafeguardReport.Cleared,
        Array.Empty<QuickAction>());
}
