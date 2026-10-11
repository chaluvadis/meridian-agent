namespace MeridianAgent.Infrastructure.External;

public sealed class NominatimOptions
{
    public const string SectionName = "Nominatim";

    /// <summary>Base URL of the Nominatim instance (env: Nominatim__BaseUrl).</summary>
    public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org";
}

public sealed class OsrmOptions
{
    public const string SectionName = "Osrm";

    /// <summary>Base URL of the OSRM routing service (env: Osrm__BaseUrl).</summary>
    public string BaseUrl { get; set; } = "https://router.project-osrm.org";

    /// <summary>Router profile to route by (env: Osrm__Profile).</summary>
    public string Profile { get; set; } = "driving";
}

public sealed class OverpassOptions
{
    public const string SectionName = "Overpass";

    /// <summary>Base URL of the Overpass API (env: Overpass__BaseUrl).</summary>
    public string BaseUrl { get; set; } = "https://overpass-api.de/api";

    /// <summary>
    /// Search radius in metres around the traveler's position (env: Overpass__RadiusMeters).
    /// Small on purpose: a concierge's list is a short walk, and a wide one is slow to answer.
    /// </summary>
    public int RadiusMeters { get; set; } = 1500;

    /// <summary>How many venues to ask for at most (env: Overpass__MaxResults).</summary>
    public int MaxResults { get; set; } = 20;

    /// <summary>Whether the live venue source is offered at all (env: Overpass__Enabled).</summary>
    public bool Enabled { get; set; } = true;
}

public sealed class WeatherOptions
{
    public const string SectionName = "Weather";

    /// <summary>Base URL of the OpenWeatherMap API (env: Weather__BaseUrl).</summary>
    public string BaseUrl { get; set; } = "https://api.openweathermap.org";

    /// <summary>OpenWeatherMap API key (env: Weather__ApiKey). Never hardcode this.</summary>
    public string ApiKey { get; set; } = string.Empty;
}
