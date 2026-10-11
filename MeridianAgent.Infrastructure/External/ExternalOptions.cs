namespace MeridianAgent.Infrastructure.External;

public sealed class NominatimOptions
{
    public const string SectionName = "Nominatim";

    /// <summary>Base URL of the Nominatim instance (env: Nominatim__BaseUrl).</summary>
    public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org";
}

public sealed class WeatherOptions
{
    public const string SectionName = "Weather";

    /// <summary>Base URL of the OpenWeatherMap API (env: Weather__BaseUrl).</summary>
    public string BaseUrl { get; set; } = "https://api.openweathermap.org";

    /// <summary>OpenWeatherMap API key (env: Weather__ApiKey). Never hardcode this.</summary>
    public string ApiKey { get; set; } = string.Empty;
}
