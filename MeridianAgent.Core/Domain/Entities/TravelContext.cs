namespace MeridianAgent.Domain.Entities;

/// <summary>The live "reality" of the traveler: where they are, what time it is, what the weather is doing.</summary>
public sealed record TravelContext
{
    [JsonPropertyName("location")]
    public string Location { get; init; } = string.Empty;

    [JsonPropertyName("local_time")]
    public string LocalTime { get; init; } = "12:00";

    [JsonPropertyName("weather_temp_c")]
    public double TemperatureC { get; init; }

    [JsonPropertyName("weather_condition")]
    public string WeatherCondition { get; init; } = "Clear";

    [JsonPropertyName("is_raining")]
    public bool IsRaining { get; init; }

    [JsonPropertyName("weather_summary")]
    public string WeatherSummary { get; init; } = string.Empty;

    /// <summary>
    /// The coordinates this reading was taken at, when the position was actually resolved. Null
    /// means only the city name was known, which is enough for weather but not for anything that
    /// needs to be nearby.
    /// </summary>
    [JsonIgnore]
    public GeoPoint? At => Latitude is null || Longitude is null
        ? null
        : new GeoPoint(Latitude.Value, Longitude.Value);

    /// <summary>
    /// The position the geocoder resolved, stored as two nullable columns so the JSON the sample
    /// context is read from stays readable without them.
    /// </summary>
    [JsonPropertyName("latitude")]
    public double? Latitude { get; init; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; init; }
}
