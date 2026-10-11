namespace MeridianAgent.Domain.Entities;

/// <summary>
/// What an agent message carries beyond its prose: the structured blocks the traveler can act on.
/// Stored as JSON alongside the message so a bookmark reopened weeks later still shows the places
/// that reply proposed.
/// </summary>
public sealed record RichContent(IReadOnlyList<RichBlock> Blocks)
{
    public static readonly RichContent Empty = new(Array.Empty<RichBlock>());

    public bool IsEmpty => Blocks.Count == 0;

    /// <summary>
    /// Projects a proposed itinerary onto chat blocks. Messages that proposed nothing return
    /// <see cref="Empty" />, which persists as null rather than an empty array.
    /// </summary>
    public static RichContent FromItinerary(ProposedItinerary? itinerary)
    {
        if (itinerary is null || !itinerary.HasVenues)
        {
            return Empty;
        }

        var blocks = itinerary.Venues
            .Select(venue => new RichBlock(
                RichBlockKind.List,
                venue.Name,
                $"{venue.Category} · {venue.TravelTimeMinutes} min away"))
            .ToArray();

        return new RichContent(blocks);
    }
}

/// <summary>
/// One structured element in a message. <see cref="RichBlockKind.Text" /> is inline prose,
/// <see cref="RichBlockKind.List" /> an item with a caption (a proposed place, say),
/// <see cref="RichBlockKind.Link" /> an address the traveler can open, and
/// <see cref="RichBlockKind.Image" /> a picture.
/// </summary>
public sealed record RichBlock(RichBlockKind Kind, string Text, string? Detail = null);

/// <summary>The kinds of structured block an agent message can carry.</summary>
public enum RichBlockKind
{
    Text,
    Link,
    Image,
    List,
}
