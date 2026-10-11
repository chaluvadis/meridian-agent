namespace MeridianAgent.Infrastructure.External;

/// <summary>A latitude/longitude pair on the globe.</summary>
public sealed record GeoPoint(double Latitude, double Longitude);

/// <summary>OpenStreetMap Nominatim geocoding (no API key required; be respectful with request rate).</summary>
public sealed class NominatimClient
{
    private readonly HttpClient _httpClient;
    private readonly NominatimOptions _options;

    public NominatimClient(HttpClient httpClient, IOptions<NominatimOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/'));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MeridianAgent/1.0 (travel concierge)");
    }

    /// <summary>
    /// Turns a place name into coordinates, or null when the name is blank, the request fails, or
    /// the answer carries no usable position.
    /// </summary>
    public async Task<GeoPoint?> GeocodeAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var url = $"search?q={Uri.EscapeDataString(query)}&format=jsonv2&limit=1";
        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var results = await response.Content
            .ReadFromJsonAsync<List<NominatimResult>>(cancellationToken)
            .ConfigureAwait(false);

        var first = results?.FirstOrDefault();
        if (first is null ||
            !double.TryParse(first.Lat, System.Globalization.CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(first.Lon, System.Globalization.CultureInfo.InvariantCulture, out var lon))
        {
            return null;
        }

        return new GeoPoint(lat, lon);
    }

    private sealed record NominatimResult
    {
        public string Lat { get; init; } = string.Empty;

        public string Lon { get; init; } = string.Empty;

        public string? DisplayName { get; init; }
    }
}
