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
                $"{venue.Category} · {venue.VenueType} · {venue.TravelTimeMinutes} min away"))
            .ToArray();

        return new RichContent(blocks);
    }
}

/// <summary>
/// The structured elements of a message that need shelter, worked out from what was stored. A
/// saved plan is only worth a weather warning if it puts the traveler outdoors, and the venue type
/// was recorded alongside the block precisely so this is a fact rather than a guess at the prose.
/// </summary>
public static class OutdoorPlan
{
    private static readonly string[] OutdoorWords =
    {
        "outdoor", "rooftop", "souk", "beach", "garden", "desert", "park", "promenade", "riverwalk", "walk",
    };

    /// <summary>
    /// Whether anything in this content puts the traveler outside. A venue typed outdoor or mixed
    /// counts; a venue typed indoor never does, so a rainy-day lunch is not a weather alarm.
    /// </summary>
    public static bool IsOutdoor(RichContent? content)
    {
        if (content is null || content.IsEmpty)
        {
            return false;
        }

        return content.Blocks.Any(block =>
            block.Kind == RichBlockKind.List &&
            (MentionsOutdoor(block.Detail) || MentionsOutdoor(block.Text)));
    }

    /// <summary>
    /// Whether the prose itself asks to be outside, for messages saved before a venue type was
    /// stored. Their own words are the only honest basis then.
    /// </summary>
    public static bool MentionsOutdoor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return OutdoorWords.Any(word =>
            text.Contains(word, StringComparison.OrdinalIgnoreCase));
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
