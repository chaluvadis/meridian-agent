namespace MeridianAgent.Infrastructure.Data;

/// <summary>Loads nearby venues from {DataDirectory}/nearby_{location-slug}.json, falling back to nearby_venues.json.</summary>
public sealed class JsonFileNearbyVenueStore : INearbyVenueStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly StorageOptions _options;
    private readonly string _dataDirectory;
    private readonly ILogger<JsonFileNearbyVenueStore> _logger;

    public JsonFileNearbyVenueStore(IOptions<StorageOptions> options, ILogger<JsonFileNearbyVenueStore> logger)
    {
        _options = options.Value;
        _dataDirectory = DataPaths.Resolve(_options.DataDirectory);
        _logger = logger;
    }

    public Task<IReadOnlyList<NearbyVenue>> GetAsync(
        string location,
        Domain.Entities.GeoPoint? at = null,
        CancellationToken cancellationToken = default)
    {
        // The file-backed list is keyed by place name; a position is not a name it can look up.
        return GetByNameAsync(location, cancellationToken);
    }

    private async Task<IReadOnlyList<NearbyVenue>> GetByNameAsync(string location, CancellationToken cancellationToken)
    {
        foreach (var candidate in CandidatePaths(location))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            await using var stream = File.OpenRead(candidate);
            var venues = await JsonSerializer
                .DeserializeAsync<List<NearbyVenue>>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            if (venues is { Count: > 0 })
            {
                _logger.LogInformation("Loaded {Count} nearby venue(s) from {Path}", venues.Count, candidate);
                return venues;
            }
        }

        _logger.LogInformation("No nearby venues found for {Location}; returning empty list", location);
        return [];
    }

    private IEnumerable<string> CandidatePaths(string location)
    {
        var slug = Slug(location);
        yield return Path.Combine(_dataDirectory, $"nearby_{slug}.json");
        yield return Path.Combine(_dataDirectory, "nearby_venues.json");
    }

    private static string Slug(string location) =>
        string.Join('_', (location ?? "unknown").ToLowerInvariant().Split(Path.GetInvalidFileNameChars()));
}
