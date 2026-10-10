using System.Globalization;
using Microsoft.Extensions.Options;

namespace MeridianAgent.Infrastructure.External;

/// <summary>OSRM (Open Source Routing Machine) - travel time and distance between two points.</summary>
public sealed class OsrmClient
{
    private readonly HttpClient _httpClient;
    private readonly OsrmOptions _options;

    public OsrmClient(HttpClient httpClient, IOptions<OsrmOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/'));
    }

    public async Task<TravelRoute?> RouteAsync(
        GeoPoint from,
        GeoPoint to,
        CancellationToken cancellationToken = default)
    {
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"route/v1/{_options.Profile}/{from.Longitude.ToString("0.#####", CultureInfo.InvariantCulture)},{from.Latitude.ToString("0.#####", CultureInfo.InvariantCulture)};{to.Longitude.ToString("0.#####", CultureInfo.InvariantCulture)},{to.Latitude.ToString("0.#####", CultureInfo.InvariantCulture)}?overview=false");

        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content
            .ReadFromJsonAsync<OsrmResponse>(cancellationToken)
            .ConfigureAwait(false);

        var route = payload?.Routes?.FirstOrDefault();
        return route is null ? null : new TravelRoute(route.Distance, route.Duration);
    }

    public sealed record TravelRoute(double DistanceMeters, double DurationSeconds)
    {
        public double DistanceKm => DistanceMeters / 1000.0;

        public double DurationMinutes => DurationSeconds / 60.0;
    }

    private sealed record OsrmResponse
    {
        public OsrmRoute[]? Routes { get; init; }
    }

    private sealed record OsrmRoute(double Distance, double Duration);
}
