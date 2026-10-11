namespace MeridianAgent.Infrastructure.Data;

using MeridianAgent.Application.Abstractions;

using MeridianAgent.Domain.Entities;

using MeridianAgent.Infrastructure.External;

/// <summary>
/// Real venues from OpenStreetMap, around a real position. This is what turns "nearby" from a claim
/// into a measurement: every venue comes back with coordinates, so distance and travel time can be
/// derived rather than asserted.
///
/// Answers are cached briefly because the public Overpass instance is slow and rate-limited, and the
/// traveler is not moving far inside a conversation.
/// </summary>
public sealed class OverpassVenueSource : ILiveVenueSource
{
    private static readonly Dictionary<string, string[]> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["amenity"] = new[] { "cafe", "restaurant", "fast_food", "bar", "pub" },
        ["tourism"] = new[] { "attraction", "museum", "artwork", "viewpoint" },
        ["leisure"] = new[] { "park", "garden" },
        ["shop"] = new[] { "mall", "marketplace" },
    };

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly OverpassOptions _options;
    private readonly ILogger<OverpassVenueSource> _logger;
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public OverpassVenueSource(
        HttpClient httpClient,
        IOptions<OverpassOptions> options,
        ILogger<OverpassVenueSource> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/'));
    }

    /// <summary>Whether this source has been asked to answer at all.</summary>
    public bool IsEnabled => _options.Enabled;

    /// <summary>
    /// Venues around a position, nearest first, or empty when there is no position or the query
    /// fails. Empty is a normal answer the caller falls back from, never an error.
    /// </summary>
    public async Task<IReadOnlyList<NearbyVenue>> GetAroundAsync(
        GeoPoint centre,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || !centre.IsPlausible)
        {
            return Array.Empty<NearbyVenue>();
        }

        var key = CacheKey(centre);

        lock (_cache)
        {
            Prune();

            if (_cache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.At < CacheDuration)
            {
                return cached.Venues;
            }
        }

        try
        {
            var response = await QueryAsync(centre, cancellationToken).ConfigureAwait(false);

            lock (_cache)
            {
                _cache[key] = new CacheEntry(response, DateTimeOffset.UtcNow);
            }

            _logger.LogInformation(
                "Overpass returned {Count} venue(s) within {Radius}m of the traveler",
                response.Count,
                _options.RadiusMeters);

            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Overpass venue query failed; falling back to the file-backed list");
            return Array.Empty<NearbyVenue>();
        }
    }

    private async Task<IReadOnlyList<NearbyVenue>> QueryAsync(
        GeoPoint centre,
        CancellationToken cancellationToken)
    {
        var query = BuildQuery(centre);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["data"] = query,
        });

        using var response = await _httpClient
            .PostAsync("interpreter", content, cancellationToken)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("elements", out var elements) ||
            elements.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<NearbyVenue>();
        }

        var venues = new List<NearbyVenue>();

        foreach (var element in elements.EnumerateArray())
        {
            var name = ReadTag(element, "name");

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            venues.Add(new NearbyVenue
            {
                Name = name,
                Category = GuessCategory(element),
                Latitude = element.GetProperty("lat").GetDouble(),
                Longitude = element.GetProperty("lon").GetDouble(),
                // OSM tags an indoor venue poorly, so the conservative read is "unknown": the
                // filter that keeps shelter treats unknown as shelter rather than assuming either.
                IsIndoor = IsIndoor(element),
                DistanceMinutes = 0,
            });
        }

        return venues
            .OrderBy(venue => venue.DistanceKmFrom(centre) ?? double.MaxValue)
            .Take(Math.Max(1, _options.MaxResults))
            .ToList();
    }

    private string BuildQuery(GeoPoint centre)
    {
        var radius = Math.Max(100, _options.RadiusMeters);
        var quote = "\"";

        // One round trip for every category of interest, and only things that actually have a name.
        var selectors = Categories.SelectMany(pair =>
            pair.Value.Select(value => $"node[{quote}{pair.Key}{quote}={quote}{value}{quote}]"));

        var around = $"(around:{radius},{Fmt(centre.Latitude)},{Fmt(centre.Longitude)})[{quote}name{quote}];";

        return $"[out:json][timeout:25];({string.Concat(selectors.Select(selector => selector + around))});out body;";
    }

    private static string GuessCategory(JsonElement element)
    {
        foreach (var key in new[] { "amenity", "tourism", "leisure", "shop" })
        {
            if (element.TryGetProperty("tags", out var tags) &&
                tags.TryGetProperty(key, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "activity";
            }
        }

        return "activity";
    }

    /// <summary>
    /// Whether the venue is indoors. OSM has no reliable tag for this, so it is read from the venue
    /// type: a café, restaurant or museum is inside, a park or a viewpoint is not, and anything
    /// ambiguous is left unknown.
    /// </summary>
    private static bool IsIndoor(JsonElement element)
    {
        var indoor = ReadTag(element, "indoor");

        if (!string.IsNullOrWhiteSpace(indoor))
        {
            return !string.Equals(indoor, "no", StringComparison.OrdinalIgnoreCase);
        }

        var category = GuessCategory(element);

        return category is "cafe" or "restaurant" or "fast_food" or "bar" or "pub" or "museum" or "mall";
    }

    private static string? ReadTag(JsonElement element, string tag) =>
        element.TryGetProperty("tags", out var tags) &&
        tags.TryGetProperty(tag, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Fmt(double value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Rounds to roughly 100m, so a traveler shifting a few metres reuses the answer instead of
    /// re-querying a rate-limited service.
    /// </summary>
    private static string CacheKey(GeoPoint centre) =>
        $"{centre.Latitude.ToString("F3", CultureInfo.InvariantCulture)}," +
        $"{centre.Longitude.ToString("F3", CultureInfo.InvariantCulture)}";

    private void Prune()
    {
        var stale = _cache
            .Where(entry => DateTimeOffset.UtcNow - entry.Value.At >= CacheDuration)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var key in stale)
        {
            _cache.Remove(key);
        }
    }

    private sealed record CacheEntry(IReadOnlyList<NearbyVenue> Venues, DateTimeOffset At);
}
