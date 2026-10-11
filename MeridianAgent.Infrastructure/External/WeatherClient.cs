namespace MeridianAgent.Infrastructure.External;

/// <summary>OpenWeatherMap current weather (env: Weather__ApiKey).</summary>
public sealed class WeatherClient
{
    private readonly HttpClient _httpClient;
    private readonly WeatherOptions _options;

    public WeatherClient(HttpClient httpClient, IOptions<WeatherOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/'));
    }

    /// <summary>False until an OpenWeatherMap key is configured; the app then runs on the sample context.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    /// <summary>
    /// Current weather at a point, or null when no key is configured, the request fails, or the
    /// payload carries neither a temperature nor a condition.
    /// </summary>
    public async Task<WeatherReading?> GetCurrentAsync(
        GeoPoint point,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"data/2.5/weather?lat={point.Latitude.ToString("0.#####", CultureInfo.InvariantCulture)}&lon={point.Longitude.ToString("0.#####", CultureInfo.InvariantCulture)}&units=metric&appid={_options.ApiKey}");

        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content
            .ReadFromJsonAsync<OpenWeatherResponse>(cancellationToken)
            .ConfigureAwait(false);

        if (payload?.Main is null || payload.Weather is not { Length: > 0 })
        {
            return null;
        }

        var condition = payload.Weather[0].Main ?? "Clear";
        var description = payload.Weather[0].Description ?? condition;

        return new WeatherReading(
            payload.Main.Temp,
            condition,
            description,
            payload.Rain is not null,
            payload.Timezone);
    }

    /// <summary>What the weather service said about one moment at one place.</summary>
    public sealed record WeatherReading(
        double TemperatureC,
        string Condition,
        string Description,
        bool IsRaining,
        int TimezoneOffsetSeconds);

    private sealed record OpenWeatherResponse
    {
        public WeatherMain? Main { get; init; }

        public WeatherDescription[]? Weather { get; init; }

        public RainVolume? Rain { get; init; }

        public int Timezone { get; init; }
    }

    private sealed record WeatherMain(double Temp);

    private sealed record WeatherDescription(string? Main, string? Description);

    private sealed record RainVolume
    {
        public double OneHour { get; init; }
    }
}
