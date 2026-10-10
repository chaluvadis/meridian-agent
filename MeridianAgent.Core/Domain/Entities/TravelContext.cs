using System.Text.Json.Serialization;

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
}
