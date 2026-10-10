namespace MeridianAgent.Infrastructure.Scheduling;

/// <summary>
/// Periodically generates proactive suggestions and pushes them into the chat as agent-initiated
/// messages. The reactive pipeline is untouched: these messages arrive through the same chat stream,
/// just visibly tagged as proactive.
/// </summary>
public sealed class ProactiveSuggestionScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProactiveChatNotifier _notifier;
    private readonly IChatActivityTracker _activity;
    private readonly ProactiveSuggestionsOptions _options;
    private readonly ILogger<ProactiveSuggestionScheduler> _logger;
    private readonly List<RecentSuggestion> _recentSignatures = new();

    public ProactiveSuggestionScheduler(
        IServiceScopeFactory scopeFactory,
        IProactiveChatNotifier notifier,
        IChatActivityTracker activity,
        IOptions<ProactiveSuggestionsOptions> options,
        ILogger<ProactiveSuggestionScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _notifier = notifier;
        _activity = activity;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Proactive suggestion scheduler running (enabled: {Enabled}, every {IntervalHours}h, min confidence {MinConfidenceScore})",
            _options.Enabled,
            _options.IntervalHours,
            _options.MinConfidenceScore);

        while (!stoppingToken.IsCancellationRequested)
        {
            await TryPublishAsync(stoppingToken).ConfigureAwait(false);

            try
            {
                await Task.Delay(MinimumInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Proactive suggestion scheduler stopped");
    }

    /// <summary>One proactive cycle (check activity, generate, filter, synthesize, push). Safe to await directly.</summary>
    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await TryPublishAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Proactive suggestion run failed");
        }
    }

    // 3 minutes is the floor so a misconfiguration cannot spin the loop; a week is the ceiling.
    private TimeSpan MinimumInterval =>
        TimeSpan.FromHours(Math.Clamp(_options.IntervalHours, 0.05, 168));

    private async Task TryPublishAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var activeWindow = TimeSpan.FromMinutes(Math.Max(1, _options.ActiveWithinMinutes));
        if (!_activity.IsActive(activeWindow))
        {
            _logger.LogDebug(
                "Traveler has been quiet for more than {Window} — skipping proactive suggestions this round",
                activeWindow);
            return;
        }

        if (await GenerateAndPublishAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }
    }

    private async Task<bool> GenerateAndPublishAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var suggestionAgent = scope.ServiceProvider.GetRequiredService<IProactiveSuggestionAgent>();
        var synthesizer = scope.ServiceProvider.GetRequiredService<ISynthesizerAgent>();
        var profileService = scope.ServiceProvider.GetRequiredService<IUserProfileService>();
        var contextProvider = scope.ServiceProvider.GetRequiredService<ITravelContextProvider>();
        var nearbyStore = scope.ServiceProvider.GetRequiredService<INearbyVenueStore>();
        var rulebookStore = scope.ServiceProvider.GetRequiredService<IRulebookStore>();

        var profile = await profileService.GetProfileAsync(cancellationToken: cancellationToken).ConfigureAwait(false)
                      ?? UserProfile.Default;
        var location = string.IsNullOrWhiteSpace(profile.HomeBase) ? profile.Destination : profile.HomeBase;

        TravelContext? context = null;
        try
        {
            context = await contextProvider.GetCurrentAsync(location, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load live context for proactive suggestions; using profile destination");
        }

        var nearby = await nearbyStore.GetAsync(context?.Location ?? location, cancellationToken).ConfigureAwait(false);
        var rulebook = await rulebookStore.GetAsync(profile.Destination, cancellationToken).ConfigureAwait(false)
                       ?? DestinationRulebook.Empty;

        var request = new ProactiveRequest(profile, context ?? default!, nearby, rulebook);

        IReadOnlyList<ProactiveSuggestion> suggestions;
        try
        {
            suggestions = await suggestionAgent.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Proactive suggestion agent produced nothing usable");
            return false;
        }

        var confident = suggestions
            .Where(s => s.ConfidenceScore >= _options.MinConfidenceScore)
            .ToArray();

        if (confident.Length == 0)
        {
            _logger.LogDebug("No proactive idea cleared the confidence bar this round");
            return false;
        }

        var signature = string.Join('|', confident.Select(s => s.Title.Trim().ToLowerInvariant()));
        if (IsOnCooldown(signature))
        {
            _logger.LogDebug("Recent suggestions are on cooldown");
            return false;
        }

        string message;
        try
        {
            message = await synthesizer
                .ExecuteProactiveAsync(confident, request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not synthesize a proactive message");
            return false;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var messageId = Guid.NewGuid().ToString();
        await _notifier.PublishAsync(messageId, message, cancellationToken).ConfigureAwait(false);
        Remember(signature);
        _logger.LogInformation(
            "Pushed proactive suggestion to chat: {MessageId} ({SuggestionCount} idea(s))",
            messageId,
            confident.Length);

        return true;
    }

    private bool IsOnCooldown(string signature)
    {
        PruneRecentSignatures();
        return _recentSignatures.Any(entry =>
            string.Equals(entry.Signature, signature, StringComparison.Ordinal));
    }

    private void Remember(string signature)
    {
        PruneRecentSignatures();
        _recentSignatures.Add(new RecentSuggestion(signature, DateTime.UtcNow));
    }

    private void PruneRecentSignatures()
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromHours(Math.Clamp(_options.CooldownHours, 0.05, 168));
        _recentSignatures.RemoveAll(entry => entry.AtUtc < cutoff);
    }

    private sealed record RecentSuggestion(string Signature, DateTime AtUtc);
}
