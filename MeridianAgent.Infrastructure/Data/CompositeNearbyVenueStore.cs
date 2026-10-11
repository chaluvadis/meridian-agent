namespace MeridianAgent.Infrastructure.Data;

using MeridianAgent.Application.Abstractions;

/// <summary>
/// The list the Search Agent is given, chosen from the best source that can answer. When the
/// traveler has shared a position, OpenStreetMap is asked for what is actually around them and the
/// driving time to each venue is measured, not asserted; otherwise the sample venue file stands in,
/// and distances stay exactly as honest as the data file that declared them.
///
/// The two sources are not equal, which is the point: the file-backed list is a sample of one city,
/// and the live one is real. Falling back is silent and correct rather than a failure.
/// </summary>
public sealed class CompositeNearbyVenueStore : INearbyVenueStore
{
    private readonly JsonFileNearbyVenueStore _fileStore;
    private readonly ILiveVenueSource _overpass;
    private readonly ITravelTimeProvider _travelTime;
    private readonly ILogger<CompositeNearbyVenueStore> _logger;

    public CompositeNearbyVenueStore(
        JsonFileNearbyVenueStore fileStore,
        ILiveVenueSource overpass,
        ITravelTimeProvider travelTime,
        ILogger<CompositeNearbyVenueStore> logger)
    {
        _fileStore = fileStore;
        _overpass = overpass;
        _travelTime = travelTime;
        _logger = logger;
    }

    public async Task<IReadOnlyList<NearbyVenue>> GetAsync(
        string location,
        GeoPoint? at = null,
        CancellationToken cancellationToken = default)
    {
        if (at is not null && _overpass.IsEnabled)
        {
            var live = await _overpass.GetAroundAsync(at, cancellationToken).ConfigureAwait(false);

            if (live.Count > 0)
            {
                return await WithMeasuredDistancesAsync(live, at, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogDebug(
                "No live venues for the traveler's position; using the sample list for {Location}",
                location);
        }

        return await _fileStore.GetAsync(location, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fills in the driving minutes the data file could not know. A venue with no route keeps its
    /// existing figure rather than being dropped: a measured answer is better, but no answer is not
    /// a reason to lose the venue.
    /// </summary>
    private async Task<IReadOnlyList<NearbyVenue>> WithMeasuredDistancesAsync(
        IReadOnlyList<NearbyVenue> venues,
        GeoPoint from,
        CancellationToken cancellationToken)
    {
        if (!_travelTime.IsConfigured)
        {
            return venues;
        }

        var measured = new List<NearbyVenue>(venues.Count);

        foreach (var venue in venues)
        {
            if (venue.At is null)
            {
                measured.Add(venue);
                continue;
            }

            var minutes = await _travelTime
                .MinutesAsync(from, venue.At, cancellationToken)
                .ConfigureAwait(false);

            measured.Add(minutes is null ? venue : venue with { DistanceMinutes = minutes.Value });
        }

        return measured;
    }
}
