namespace MeridianAgent.Web.Components;

using Microsoft.JSInterop;

/// <summary>
/// Asks the browser where the traveler is. Opt-in and permission-gated: a refusal, a timeout, a
/// page rendered before the circuit exists or a browser with no geolocation all come back as "no
/// position", which is a perfectly good answer the rest of the app already copes with.
/// </summary>
public sealed class GeolocationInterop
{
    private readonly IJSRuntime _runtime;

    public GeolocationInterop(IJSRuntime runtime) => _runtime = runtime;

    /// <summary>The traveler's position, or null when it could not be found out.</summary>
    public async Task<Domain.Entities.GeoPoint?> GetPositionAsync()
    {
        try
        {
            var position = await _runtime.InvokeAsync<Position?>("getCurrentPosition");

            return position is null ? null : new Domain.Entities.GeoPoint(position.Latitude, position.Longitude);
        }
        catch (JSException)
        {
            // The script is missing, or the permission prompt threw.
            return null;
        }
        catch (JSDisconnectedException)
        {
            // The page is gone.
            return null;
        }
        catch (InvalidOperationException)
        {
            // Prerendering: there is no circuit yet to run JS in.
            return null;
        }
    }

    private sealed record Position(double Latitude, double Longitude);
}
