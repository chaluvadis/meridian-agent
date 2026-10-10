namespace MeridianAgent.Infrastructure.External;

/// <summary>
/// Builds the live TravelContext from Nominatim + OpenWeatherMap. When either service is
/// unavailable or no API key is configured, falls back to {DataDirectory}/travel_context.json,
/// then to a fixed sample so the pipeline always has a reality to work with.
/// </summary>
public sealed class LiveTravelContextProvider(
    NominatimClient nominatim,
    WeatherClient weather,
    IOptions<StorageOptions> storage,
    ILogger<LiveTravelContextProvider> logger) : ITravelContextProvider
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// How long a live context stays reusable. Both the chat header and every pipeline query ask
    /// for the same location, and prerendering means the page loads twice per visit, so without
    /// this each page load costs a geocode and a weather round trip per caller.
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly NominatimClient _nominatim = nominatim;
    private readonly WeatherClient _weather = weather;
    private readonly string _dataDirectory = DataPaths.Resolve(storage.Value.DataDirectory);
    private readonly ILogger<LiveTravelContextProvider> _logger = logger;

    /// <summary>A context and the moment it was stored, keyed by the location it describes.</summary>
    private sealed record Entry(TravelContext Context, DateTimeOffset StoredAt);

    /// <summary>
    /// Resolved contexts by location. Each entry carries its own age, so one location being
    /// refreshed cannot extend the life of another.
    /// </summary>
    private readonly Dictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Refreshes currently in flight, keyed the same way, so callers share one round trip.</summary>
    private readonly Dictionary<string, Task<TravelContext>> _refreshing = new(StringComparer.OrdinalIgnoreCase);

    public async Task<TravelContext> GetCurrentAsync(string location, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return await LoadSampleAsync(cancellationToken).ConfigureAwait(false);
        }

        // Trim once so spellings of the same place share an entry instead of accumulating.
        var key = location.Trim();
        Task<TravelContext>? refresh = null;

        lock (_cache)
        {
            if (_cache.TryGetValue(key, out var cached) && IsFresh(cached))
            {
                return cached.Context;
            }

            if (!_refreshing.TryGetValue(key, out refresh))
            {
                refresh = RefreshAsync(location, key, cancellationToken);
                _refreshing[key] = refresh;
            }
        }

        try
        {
            return await refresh.ConfigureAwait(false);
        }
        finally
        {
            lock (_cache)
            {
                // Only detach the task this caller owned; a later one may already have started.
                if (_refreshing.TryGetValue(key, out var pending) && pending == refresh)
                {
                    _refreshing.Remove(key);
                }
            }
        }
    }

    /// <summary>
    /// Calls the weather services and caches the result, fallback included. Never throws, so every
    /// caller awaiting it sees the same answer rather than one failing on the caller's behalf.
    /// </summary>
    private async Task<TravelContext> RefreshAsync(string location, string key, CancellationToken cancellationToken)
    {
        TravelContext resolved;

        try
        {
            var point = await _nominatim.GeocodeAsync(location, cancellationToken).ConfigureAwait(false);
            var weather = point is null
                ? null
                : await _weather.GetCurrentAsync(point, cancellationToken).ConfigureAwait(false);

            if (point is not null && weather is not null)
            {
                var localTime = DateTimeOffset
                    .UtcNow
                    .ToOffset(TimeSpan.FromSeconds(weather.TimezoneOffsetSeconds))
                    .ToString("HH:mm", CultureInfo.InvariantCulture);

                resolved = new TravelContext
                {
                    Location = location,
                    LocalTime = localTime,
                    TemperatureC = weather.TemperatureC,
                    WeatherCondition = weather.Condition,
                    IsRaining = weather.IsRaining,
                    WeatherSummary = $"{weather.Description}, {weather.TemperatureC.ToString("0", CultureInfo.InvariantCulture)}°C",
                };

                return StoreInCache(key, resolved);
            }

            _logger.LogInformation("Live weather unavailable for {Location}; using the sample context", location);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not build a live context for {Location}; using the sample context", location);
        }

        resolved = await LoadSampleAsync(cancellationToken).ConfigureAwait(false);

        // The fallback is deterministic, so it is cached on the same terms as a live reading: a
        // missing API key must not turn every page load into another geocode round trip.
        return StoreInCache(key, resolved);
    }

    private static bool IsFresh(Entry entry) => DateTimeOffset.UtcNow - entry.StoredAt < CacheDuration;

    private TravelContext StoreInCache(string key, TravelContext context)
    {
        lock (_cache)
        {
            // Drop everything already stale, so the dictionary tracks the places this app is being
            // used rather than every string it has ever been asked about.
            var obsolete = new List<string>();

            foreach (var cached in _cache)
            {
                if (!IsFresh(cached.Value))
                {
                    obsolete.Add(cached.Key);
                }
            }

            foreach (var stale in obsolete)
            {
                _cache.Remove(stale);
            }

            _cache[key] = new Entry(context, DateTimeOffset.UtcNow);
            return context;
        }
    }

    private async Task<TravelContext> LoadSampleAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_dataDirectory, "travel_context.json");
        if (File.Exists(path))
        {
            await using var stream = File.OpenRead(path);
            var sample = await JsonSerializer.DeserializeAsync<TravelContext>(stream, Options, cancellationToken).ConfigureAwait(false);
            if (sample is not null)
            {
                return sample;
            }
        }

        return new TravelContext
        {
            Location = "Dubai, UAE",
            LocalTime = "20:45",
            TemperatureC = 31,
            WeatherCondition = "Clear",
            IsRaining = false,
            WeatherSummary = "Warm, humid evening, 31°C - the city is still wide awake",
        };
    }
}
