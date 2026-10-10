using System.Globalization;
using System.Text.Json;
using MeridianAgent.Application.Abstractions;
using MeridianAgent.Domain.Entities;
using MeridianAgent.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MeridianAgent.Infrastructure.External;

/// <summary>
/// Builds the live TravelContext from Nominatim + OpenWeatherMap. When either service is
/// unavailable or no API key is configured, falls back to {DataDirectory}/travel_context.json,
/// then to a fixed sample so the pipeline always has a reality to work with.
/// </summary>
public sealed class LiveTravelContextProvider : ITravelContextProvider
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly NominatimClient _nominatim;
    private readonly WeatherClient _weather;
    private readonly string _dataDirectory;
    private readonly ILogger<LiveTravelContextProvider> _logger;

    public LiveTravelContextProvider(
        NominatimClient nominatim,
        WeatherClient weather,
        IOptions<StorageOptions> storage,
        ILogger<LiveTravelContextProvider> logger)
    {
        _nominatim = nominatim;
        _weather = weather;
        _dataDirectory = DataPaths.Resolve(storage.Value.DataDirectory);
        _logger = logger;
    }

    public async Task<TravelContext> GetCurrentAsync(string location, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(location))
        {
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

                    return new TravelContext
                    {
                        Location = location,
                        LocalTime = localTime,
                        TemperatureC = weather.TemperatureC,
                        WeatherCondition = weather.Condition,
                        IsRaining = weather.IsRaining,
                        WeatherSummary = $"{weather.Description}, {weather.TemperatureC.ToString("0", CultureInfo.InvariantCulture)}°C",
                    };
                }

                _logger.LogInformation("Live weather unavailable for {Location}; using the sample context", location);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not build a live context for {Location}; using the sample context", location);
            }
        }

        return await LoadSampleAsync(cancellationToken).ConfigureAwait(false);
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
            Location = "Sample City",
            LocalTime = "21:40",
            TemperatureC = 31,
            WeatherCondition = "Rain",
            IsRaining = true,
            WeatherSummary = "Warm evening rain, 31°C - a good night for staying indoors",
        };
    }
}
