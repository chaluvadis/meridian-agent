namespace MeridianAgent.Infrastructure.External;

using MeridianAgent.Application.Abstractions;

using MeridianAgent.Domain.Entities;

/// <summary>
/// Driving times from OSRM, so a minute's walk is a fact rather than a number somebody typed into a
/// data file. Straight-line distance cannot tell a river or a highway apart, which is the whole
/// reason this exists.
/// </summary>
public sealed class OsrmTravelClient : ITravelTimeProvider
{
    private readonly HttpClient _httpClient;
    private readonly OsrmOptions _options;
    private readonly ILogger<OsrmTravelClient> _logger;

    public OsrmTravelClient(HttpClient httpClient, IOptions<OsrmOptions> options, ILogger<OsrmTravelClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/'));
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.BaseUrl);

    /// <summary>
    /// Driving minutes between two positions, or null. Null is a legitimate answer — a route across
    /// water or off-road has no driving time — so callers must be able to cope without one.
    /// </summary>
    public async Task<int?> MinutesAsync(GeoPoint from, GeoPoint to, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || !from.IsPlausible || !to.IsPlausible)
        {
            return null;
        }

        // OSRM takes longitude first, and the two points are joined with a semicolon.
        var url =
            $"route/v1/{_options.Profile}/{Format(to)};{Format(from)}?overview=false&alternatives=false&steps=false";

        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            // "Ok" is the only code that carries a route; "NoRoute" happens across water.
            if (root.TryGetProperty("code", out var code) &&
                code.ValueKind == JsonValueKind.String &&
                !string.Equals(code.GetString(), "Ok", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("OSRM returned {Code} for a route", code.GetString());
                return null;
            }

            if (!root.TryGetProperty("routes", out var routes) ||
                routes.ValueKind != JsonValueKind.Array ||
                routes.GetArrayLength() == 0)
            {
                return null;
            }

            var seconds = routes[0].GetProperty("duration").GetDouble();

            return Math.Max(1, (int)Math.Round(seconds / 60));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Osrm route request timed out");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OSRM route request failed (returning null so callers keep their fallback)");
            return null;
        }
    }

    private static string Format(GeoPoint point) =>
        $"{point.Longitude.ToString(CultureInfo.InvariantCulture)},{point.Latitude.ToString(CultureInfo.InvariantCulture)}";
}
