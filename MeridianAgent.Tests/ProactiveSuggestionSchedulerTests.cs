using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MeridianAgent.Tests;

public class ProactiveSuggestionSchedulerTests
{
    private static ProactiveSuggestionScheduler CreateScheduler(
        RecordingChatNotifier notifier,
        FakeChatActivityTracker activity,
        Action<ProactiveSuggestionsOptions>? configure = null,
        StubTravelContextProvider? contextProvider = null,
        FakeProactiveSuggestionAgent? suggestionAgent = null,
        FakeSynthesizerAgent? synthesizer = null)
    {
        var options = new ProactiveSuggestionsOptions();
        configure?.Invoke(options);

        var services = new Dictionary<Type, object>
        {
            [typeof(IProactiveSuggestionAgent)] = suggestionAgent ?? new FakeProactiveSuggestionAgent(),
            [typeof(ISynthesizerAgent)] = synthesizer ?? new FakeSynthesizerAgent(),
            [typeof(IUserProfileService)] = new StubUserProfileService(),
            [typeof(IRulebookStore)] = new StubRulebookStore(),
            [typeof(INearbyVenueStore)] = new StubNearbyVenueStore(),
            [typeof(ITravelContextProvider)] = contextProvider ?? new StubTravelContextProvider(),
        };

        // The pivot service is driven by the same weather stub the suggestion path uses, so a weather
        // alarm is only ever produced in these tests when the stub says the weather has turned.
        var pivots = new WeatherPivotService(
            new StubScopeFactory(new StubServiceProvider(services)),
            notifier,
            new SignatureCooldown(TimeSpan.FromHours(4)),
            Options.Create(new WeatherPivotOptions()),
            NullLogger<WeatherPivotService>.Instance);

        return new ProactiveSuggestionScheduler(
            new StubScopeFactory(new StubServiceProvider(services)),
            notifier,
            activity,
            new SignatureCooldown(TimeSpan.FromHours(4)),
            pivots,
            Options.Create(options),
            NullLogger<ProactiveSuggestionScheduler>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_PushesFormattedSuggestionToTheChat()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var notifier = new RecordingChatNotifier();
        var activity = new FakeChatActivityTracker { IsActiveResult = true };
        var synthesizer = new FakeSynthesizerAgent { ProactiveResponse = "A calm rooftop is six minutes away." };
        var scheduler = CreateScheduler(
            notifier,
            activity,
            options => options.IntervalHours = 4,
            synthesizer: synthesizer);

        await scheduler.StartAsync(cts.Token);
        await notifier.WaitForPushAsync(TimeSpan.FromSeconds(5));

        var published = Assert.Single(notifier.Published);
        Assert.Equal("A calm rooftop is six minutes away.", published.Content);
        Assert.False(string.IsNullOrWhiteSpace(published.MessageId));

        // Only suggestions above the confidence bar reach the synthesizer.
        var (suggestions, request) = Assert.Single(synthesizer.ProactiveRequests);
        Assert.Equal(2, suggestions.Count);
        Assert.All(suggestions, s => Assert.True(s.ConfidenceScore >= 0.55, "every suggestion clears the bar"));
        Assert.Equal("Dubai, UAE", request.Profile.Destination);

        await scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsWhenTheTravelerIsNotActive()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var notifier = new RecordingChatNotifier();
        var activity = new FakeChatActivityTracker { IsActiveResult = false };
        var scheduler = CreateScheduler(notifier, activity, options => options.IntervalHours = 4);

        await scheduler.StartAsync(cts.Token);
        await Task.Delay(300);
        await scheduler.StopAsync(CancellationToken.None);

        Assert.Empty(notifier.Published);
        var window = Assert.Single(activity.Windows);
        Assert.Equal(TimeSpan.FromMinutes(30), window);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsWhenNothingClearsTheConfidenceBar()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var notifier = new RecordingChatNotifier();
        var activity = new FakeChatActivityTracker { IsActiveResult = true };
        var scheduler = CreateScheduler(
            notifier,
            activity,
            options =>
            {
                options.IntervalHours = 4;
                options.MinConfidenceScore = 0.95;
            });

        await scheduler.StartAsync(cts.Token);
        await Task.Delay(300);
        await scheduler.StopAsync(CancellationToken.None);

        Assert.Empty(notifier.Published);
    }

    [Fact]
    public async Task ExecuteAsync_StillPushesWhenLiveContextFails()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var notifier = new RecordingChatNotifier();
        var activity = new FakeChatActivityTracker { IsActiveResult = true };
        var suggestions = new FakeProactiveSuggestionAgent();
        var scheduler = CreateScheduler(
            notifier,
            activity,
            options => options.IntervalHours = 4,
            contextProvider: new StubTravelContextProvider
            {
                Error = new HttpRequestException("weather is down"),
            },
            suggestionAgent: suggestions);

        await scheduler.StartAsync(cts.Token);
        await notifier.WaitForPushAsync(TimeSpan.FromSeconds(5));
        await scheduler.StopAsync(CancellationToken.None);

        var request = Assert.Single(suggestions.Requests);
        Assert.Equal(default, request.Context);
        Assert.Equal("Dubai, UAE", request.Profile.Destination);
        Assert.Single(notifier.Published);
    }

    [Fact]
    public async Task RunOnceAsync_DoesNotRepeatTheSameSuggestionWhileOnCooldown()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var notifier = new RecordingChatNotifier();
        var activity = new FakeChatActivityTracker { IsActiveResult = true };
        var suggestionAgent = new FakeProactiveSuggestionAgent();
        var scheduler = CreateScheduler(
            notifier,
            activity,
            options => options.CooldownHours = 1,
            suggestionAgent: suggestionAgent);

        await scheduler.RunOnceAsync(cts.Token);
        Assert.Single(notifier.Published);

        // The same ideas again inside the cooldown window: suppressed.
        await scheduler.RunOnceAsync(cts.Token);
        Assert.Single(notifier.Published);

        // New ideas: allowed through.
        suggestionAgent.Result = new[]
        {
            new ProactiveSuggestion(
                "a late shawarma run",
                "the stall by your hotel stays open past midnight",
                0.74),
        };

        await scheduler.RunOnceAsync(cts.Token);
        Assert.Equal(2, notifier.Published.Count);
        Assert.Contains("It's cooling down", notifier.Published[1].Content);
    }
}

/// <summary>Hands the scheduler a fixed provider without pulling in a DI container.</summary>
public sealed class StubScopeFactory(IServiceProvider provider) : IServiceScopeFactory
{
    public IServiceScope CreateScope() => new StubScope(provider);

    private sealed class StubScope(IServiceProvider provider) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = provider;

        public void Dispose()
        {
        }
    }
}

public sealed class StubServiceProvider(Dictionary<Type, object> services) : IServiceProvider
{
    public object? GetService(Type serviceType) =>
        services.TryGetValue(serviceType, out var service) ? service : null;
}
