namespace MeridianAgent.Domain.Entities;

/// <summary>
/// A position on the globe. The one fact that makes "nearby" mean something: until a venue, a
/// neighborhood and a traveler all have one, distances are only whatever a data file claims.
/// </summary>
/// <param name="Latitude">Degrees north or south of the equator.</param>
/// <param name="Longitude">Degrees east or west of the prime meridian.</param>
public sealed record GeoPoint(double Latitude, double Longitude)
{
    /// <summary>Earth's mean radius in kilometres, the basis for the haversine below.</summary>
    private const double EarthRadiusKm = 6371.0088;

    /// <summary>
    /// Straight-line distance to another point, in kilometres. Straight-line, not driving: the
    /// difference matters over water and across a highway, which is why a routing client exists.
    /// </summary>
    public double DistanceKmTo(GeoPoint other)
    {
        var latA = ToRadians(Latitude);
        var latB = ToRadians(other.Latitude);
        var deltaLat = latB - latA;
        var deltaLon = ToRadians(other.Longitude - Longitude);

        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
            Math.Cos(latA) * Math.Cos(latB) * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>Whether this is a real position rather than the zero a missing one defaults to.</summary>
    public bool IsPlausible =>
        Math.Abs(Latitude) <= 90 && Math.Abs(Longitude) <= 180 && !(Latitude == 0 && Longitude == 0);

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
