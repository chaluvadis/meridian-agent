using MeridianAgent.Domain.Entities;

namespace MeridianAgent.Domain.AgentContracts;

/// <summary>Output of the Search Agent: 1-3 proposed venues.</summary>
public sealed record ProposedItinerary(IReadOnlyList<Venue> Venues)
{
    public static readonly ProposedItinerary Empty = new(Array.Empty<Venue>());

    public bool HasVenues => Venues.Count > 0;
}
