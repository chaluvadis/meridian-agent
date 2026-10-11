namespace MeridianAgent.Domain.Entities;

/// <summary>A venue that exists physically near the traveler, fed to the Search Agent as ground truth.</summary>
public sealed record NearbyVenue
{
    [JsonPropertyName("venue_name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("is_open_now")]
    public bool IsOpenNow { get; init; } = true;

    [JsonPropertyName("is_indoor")]
    public bool IsIndoor { get; init; }

    [JsonPropertyName("dietary_options")]
    public IReadOnlyList<string> DietaryOptions { get; init; } = Array.Empty<string>();

    [JsonPropertyName("distance_minutes")]
    public int DistanceMinutes { get; init; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; init; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; init; }

    /// <summary>Whether this venue can be measured against a position at all.</summary>
    [JsonIgnore]
    public bool HasCoordinates => Latitude is not null && Longitude is not null;

    /// <summary>This venue's position, or null when it was never given one.</summary>
    [JsonIgnore]
    public GeoPoint? At => HasCoordinates ? new GeoPoint(Latitude!.Value, Longitude!.Value) : null;

    /// <summary>
    /// Straight-line distance from a position, in kilometres, or null when either side has none.
    /// Kilometres rather than minutes: a straight line does not know about traffic.
    /// </summary>
    public double? DistanceKmFrom(GeoPoint from) => At?.DistanceKmTo(from);
}
