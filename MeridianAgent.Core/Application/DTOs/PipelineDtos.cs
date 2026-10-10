using MeridianAgent.Domain.AgentContracts;
using MeridianAgent.Domain.Entities;

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

/// <summary>Non-streaming result of a full pipeline run.</summary>
public sealed record TravelResponse(
    string ResponseText,
    ProposedItinerary Itinerary,
    SafeguardReport SafeguardReport);
