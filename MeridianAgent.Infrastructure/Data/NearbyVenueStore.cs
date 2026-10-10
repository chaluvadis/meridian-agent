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

    public async Task<IReadOnlyList<NearbyVenue>> GetAsync(string location, CancellationToken cancellationToken = default)
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
        return Array.Empty<NearbyVenue>();
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
