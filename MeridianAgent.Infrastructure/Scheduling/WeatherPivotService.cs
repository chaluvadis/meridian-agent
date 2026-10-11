namespace MeridianAgent.Infrastructure.Scheduling;

using MeridianAgent.Application;
using MeridianAgent.Application.Abstractions;
using MeridianAgent.Domain.AgentContracts;
using MeridianAgent.Domain.Entities;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Watches the weather against what the traveler has saved, and speaks up when a plan needs
/// sheltering. A suggestion can wait until they are quiet; a thunderstorm over a rooftop dinner
/// cannot, so this runs before the suggestion path and pre-empts it.
///
/// The pivot never invents a rescue. It reads the conditions once, finds the saved plans that put
/// the traveler outdoors, and hands the Synthesizer only indoor places that are already nearby.
/// </summary>
public sealed class WeatherPivotService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProactiveChatNotifier _notifier;
    private readonly ILogger<WeatherPivotService> _logger;
    private readonly SignatureCooldown _cooldown;
    private readonly WeatherPivotOptions _options;

    public WeatherPivotService(
        IServiceScopeFactory scopeFactory,
        IProactiveChatNotifier notifier,
        SignatureCooldown cooldown,
        IOptions<WeatherPivotOptions> options,
        ILogger<WeatherPivotService> logger)
    {
        _scopeFactory = scopeFactory;
        _notifier = notifier;
        _cooldown = cooldown;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Checks today's saved plans against the weather and, when they conflict, pushes one message.
    /// Safe to await directly: any failure is logged and returns nothing rather than taking the
    /// suggestion cycle down with it.
    /// </summary>
    public async Task<bool> TryPublishPivotAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return false;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var provider = scope.ServiceProvider;

            var profileService = provider.GetRequiredService<IUserProfileService>();
            var contextProvider = provider.GetRequiredService<ITravelContextProvider>();
            var history = provider.GetRequiredService<IChatHistoryService>();
            var nearbyStore = provider.GetRequiredService<INearbyVenueStore>();
            var synthesizer = provider.GetRequiredService<ISynthesizerAgent>();

            var profile = await profileService
                .GetProfileAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false)
                ?? UserProfile.Default;

            var location = profile.EffectiveLocation;
            var context = await ReadContextAsync(contextProvider, location, profile.Coordinates, cancellationToken).ConfigureAwait(false);

            if (context is null || !Severity.Of(context.WeatherCondition, context.IsRaining, context.TemperatureC).IsSevere)
            {
                return false;
            }

            var outdoor = await FindOutdoorSavesAsync(history, cancellationToken).ConfigureAwait(false);

            if (outdoor.Count == 0)
            {
                _logger.LogDebug(
                    "Weather is {Condition} but nothing saved today asks to be outside",
                    context.WeatherCondition);
                return false;
            }

            var indoor = await IndoorShelterAsync(nearbyStore, location, profile.Coordinates, outdoor, cancellationToken).ConfigureAwait(false);

            var signature = string.Join(
                '|',
                context.WeatherCondition,
                outdoor.Select(saved => saved.VenueName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

            if (_cooldown.IsOnCooldown(signature))
            {
                _logger.LogDebug("The same weather conflict is still on cooldown");
                return false;
            }

            var pivot = new WeatherPivot(
                context.WeatherCondition,
                context.TemperatureC,
                context.IsRaining,
                context.LocalTime,
                context.Location,
                Describe(outdoor),
                indoor);

            var message = await synthesizer
                .ExecuteWeatherPivotAsync(pivot, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            var messageId = Guid.NewGuid().ToString();
            await _notifier
                .PublishAsync(messageId, message, IProactiveChatNotifier.Kinds.WeatherPivot, cancellationToken)
                .ConfigureAwait(false);

            _cooldown.Remember(signature);
            _logger.LogInformation(
                "Pushed weather pivot for {Condition} over {Count} outdoor plan(s): {MessageId}",
                context.WeatherCondition,
                outdoor.Count,
                messageId);

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Weather pivot check failed; the suggestion cycle continues");
            return false;
        }
    }

    /// <summary>Today's weather, or null when it could not be read at all.</summary>
    private async Task<TravelContext?> ReadContextAsync(
        ITravelContextProvider contextProvider,
        string location,
        GeoPoint? at,
        CancellationToken cancellationToken)
    {
        try
        {
            return await contextProvider.GetCurrentAsync(location, at, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read live weather for the pivot check");
            return null;
        }
    }

    /// <summary>
    /// Today's saved answers that put the traveler outdoors. A save with no places in it is not a
    /// plan to shelter — it is a fact the agent told them, and the weather has nothing to do with it.
    /// </summary>
    private static async Task<List<OutdoorSave>> FindOutdoorSavesAsync(
        IChatHistoryService history,
        CancellationToken cancellationToken)
    {
        var saved = await history.GetFavoritesForCurrentSessionAsync(cancellationToken).ConfigureAwait(false);

        var outdoor = new List<OutdoorSave>();

        foreach (var message in saved)
        {
            var rich = ChatMessageCodec.ReadRichContent(message);
            var putsThemOutdoors = OutdoorPlan.IsOutdoor(rich) || OutdoorPlan.MentionsOutdoor(message.Content);

            if (!putsThemOutdoors)
            {
                continue;
            }

            var venue = rich?.Blocks.FirstOrDefault(block => block.Kind == RichBlockKind.List)?.Text
                ?? FirstNamedPlace(message.Content)
                ?? "your saved plan";

            outdoor.Add(new OutdoorSave(venue, message.Timestamp));
        }

        return outdoor;
    }

    private static async Task<List<string>> IndoorShelterAsync(
        INearbyVenueStore nearbyStore,
        string location,
        GeoPoint? at,
        List<OutdoorSave> outdoor,
        CancellationToken cancellationToken)
    {
        var nearby = await nearbyStore.GetAsync(location, at, cancellationToken).ConfigureAwait(false);

        var shelter = nearby
            .Where(venue => venue.IsIndoor)
            .Where(venue => !outdoor.Any(saved =>
                string.Equals(saved.VenueName, venue.Name, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(venue => venue.DistanceMinutes)
            .Take(3)
            .Select(venue => $"{venue.Name} ({venue.Category}, {venue.DistanceMinutes} min away)")
            .ToList();

        return shelter;
    }

    private static string Describe(List<OutdoorSave> outdoor)
    {
        var places = string.Join(", ", outdoor.Select(saved => saved.VenueName).Distinct(StringComparer.OrdinalIgnoreCase));

        return outdoor.Count == 1
            ? $"The plan saved today includes an outdoor stop at {places}."
            : $"The plans saved today include outdoor stops at {places}.";
    }

    /// <summary>The first place a message names, for a save whose blocks are gone.</summary>
    private static string? FirstNamedPlace(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var quoted = System.Text.RegularExpressions.Regex.Match(content, "['\"“]([^'\"”]{3,60})['\"”]");

        return quoted.Success ? quoted.Groups[1].Value.Trim() : null;
    }

    private sealed record OutdoorSave(string VenueName, DateTime SavedAt);
}

/// <summary>What turns a weather reading into an alarm.</summary>
public static class Severity
{
    /// <summary>
    /// Rain, a storm, sand, or heat past the threshold. Anything comfortably inside is not worth
    /// interrupting a conversation over — a pivot is for weather that would spoil the plan.
    /// </summary>
    public static Reading Of(string? condition, bool isRaining, double temperatureC)
    {
        var text = (condition ?? string.Empty).Trim();
        var severe = isRaining
            || text.Contains("rain", StringComparison.OrdinalIgnoreCase)
            || text.Contains("storm", StringComparison.OrdinalIgnoreCase)
            || text.Contains("sand", StringComparison.OrdinalIgnoreCase)
            || text.Contains("dust", StringComparison.OrdinalIgnoreCase)
            || temperatureC >= HeatThresholdC;

        return new Reading(severe, text, temperatureC, isRaining);
    }

    private const double HeatThresholdC = 40;

    public sealed record Reading(bool IsSevere, string Condition, double TemperatureC, bool IsRaining);
}

/// <summary>Configuration for the weather pivot check.</summary>
public sealed class WeatherPivotOptions
{
    public const string SectionName = "WeatherPivot";

    /// <summary>Whether the pivot check runs at all (env: WeatherPivot__Enabled).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hours before the same weather conflict may be raised again (env: WeatherPivot__CooldownHours).</summary>
    public double CooldownHours { get; set; } = 4;

    /// <summary>Days a signature is kept before it is pruned (env: WeatherPivot__MemoryDays).</summary>
    public double MemoryDays { get; set; } = 2;
}
