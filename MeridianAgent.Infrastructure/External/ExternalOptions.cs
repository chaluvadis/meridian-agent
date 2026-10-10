namespace MeridianAgent.Infrastructure.External;

public sealed class NominatimOptions
{
    public const string SectionName = "Nominatim";

    public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org";
}

public sealed class OsrmOptions
{
    public const string SectionName = "Osrm";

    public string BaseUrl { get; set; } = "https://router.project-osrm.org";

    public string Profile { get; set; } = "driving";
}

public sealed class WeatherOptions
{
    public const string SectionName = "Weather";

    public string BaseUrl { get; set; } = "https://api.openweathermap.org";

    /// <summary>OpenWeatherMap API key (env: Weather__ApiKey). Never hardcode this.</summary>
    public string ApiKey { get; set; } = string.Empty;
}
