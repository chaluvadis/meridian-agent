namespace MeridianAgent.Tests;

using System.Globalization;

using MeridianAgent.Domain.AgentContracts;
using MeridianAgent.Domain.Entities;

using MeridianAgent.Infrastructure.Data;
using MeridianAgent.Infrastructure.Scheduling;
using MeridianAgent.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

public class WeatherPivotTests
{
    private static readonly DateTime SavedAt = new(2026, 10, 12, 11, 30, 0, DateTimeKind.Local);

    private static ChatMessageEntity OutdoorSave(string venue, string detail = "Restaurant · Outdoor · 10 min away") =>
        new()
        {
            Id = 1,
            Content = $"Top pick: {venue}.",
            Timestamp = SavedAt,
            IsFavorite = true,
            RichContentJson = $"{{\"blocks\":[{{\"kind\":3,\"text\":\"{venue}\",\"detail\":\"{detail}\"}}]}}",
        };

    private static WeatherPivotService CreatePivots(
        RecordingChatNotifier notifier,
        StubTravelContextProvider? context = null,
        StubNearbyVenueStore? venues = null,
        StubChatHistoryService? history = null,
        FakeSynthesizerAgent? synthesizer = null)
    {
        var services = new Dictionary<Type, object>
        {
            [typeof(IProactiveSuggestionAgent)] = new FakeProactiveSuggestionAgent(),
            [typeof(ISynthesizerAgent)] = synthesizer ?? new FakeSynthesizerAgent(),
            [typeof(IUserProfileService)] = new StubUserProfileService(),
            [typeof(IRulebookStore)] = new StubRulebookStore(),
            [typeof(INearbyVenueStore)] = venues ?? new StubNearbyVenueStore(),
            [typeof(ITravelContextProvider)] = context ?? new StubTravelContextProvider(),
            [typeof(IChatHistoryService)] = history ?? new StubChatHistoryService(),
        };

        return new WeatherPivotService(
            new StubScopeFactory(new StubServiceProvider(services)),
            notifier,
            new SignatureCooldown(TimeSpan.FromHours(4)),
            Microsoft.Extensions.Options.Options.Create(new WeatherPivotOptions()),
            NullLogger<WeatherPivotService>.Instance);
    }

    private static TravelContext Context(string condition, double temperatureC, bool raining) =>
        new()
        {
            Location = "Downtown Dubai",
            LocalTime = "20:45",
            WeatherCondition = condition,
            TemperatureC = temperatureC,
            IsRaining = raining,
            WeatherSummary = $"{condition}, {temperatureC.ToString("0", CultureInfo.InvariantCulture)}°C",
        };

    [Fact]
    public async Task SpeaksUpWhenRainWouldReachASavedOutdoorPlan()
    {
        var notifier = new RecordingChatNotifier();
        var history = new StubChatHistoryService { FavoritesForToday = new[] { OutdoorSave("Al Fahidi Heritage Walk") } };
        var pivots = CreatePivots(
            notifier,
            new StubTravelContextProvider { Context = Context("Rain", 29, true) },
            history: history);

        await pivots.TryPublishPivotAsync(CancellationToken.None);

        var published = Assert.Single(notifier.Published);
        Assert.Equal(IProactiveChatNotifier.Kinds.WeatherPivot, published.Kind);
    }

    [Fact]
    public async Task StaysQuietWhenTheWeatherIsComfortable()
    {
        var notifier = new RecordingChatNotifier();
        var history = new StubChatHistoryService { FavoritesForToday = new[] { OutdoorSave("Al Fahidi Heritage Walk") } };
        var pivots = CreatePivots(notifier, new StubTravelContextProvider { Context = Context("Clear", 31, false) }, history: history);

        Assert.False(await pivots.TryPublishPivotAsync(CancellationToken.None));
        Assert.Empty(notifier.Published);
    }

    [Fact]
    public async Task StaysQuietWhenTheSavesAreAllIndoors()
    {
        var notifier = new RecordingChatNotifier();
        var history = new StubChatHistoryService
        {
            FavoritesForToday = new[] { OutdoorSave("Karak House", "Cafe · Indoor · 6 min away") },
        };
        var pivots = CreatePivots(
            notifier,
            new StubTravelContextProvider { Context = Context("Thunderstorm", 27, true) },
            history: history);

        Assert.False(await pivots.TryPublishPivotAsync(CancellationToken.None));
        Assert.Empty(notifier.Published);
    }

    [Fact]
    public async Task HeatPastTheThresholdIsAlsoAnAlarm()
    {
        var notifier = new RecordingChatNotifier();
        var history = new StubChatHistoryService { FavoritesForToday = new[] { OutdoorSave("Jumeirah Beach") } };
        var pivots = CreatePivots(notifier, new StubTravelContextProvider { Context = Context("Clear", 42, false) }, history: history);

        Assert.True(await pivots.TryPublishPivotAsync(CancellationToken.None));
        Assert.Single(notifier.Published);
    }

    [Fact]
    public async Task AMixedVenueWithAShelteredSectionCountsAsShelter()
    {
        // Same rule the Search Agent negotiates by: a mixed venue with an air-conditioned or
        // shaded section is a dry option, not an outdoor one.
        var recording = new FakeSynthesizerAgent();
        var history = new StubChatHistoryService
        {
            FavoritesForToday = new[] { OutdoorSave("Al Fahidi Heritage Walk", "Activity · Outdoor · 10 min away") },
        };
        var venues = new StubNearbyVenueStore
        {
            Venues = new[]
            {
                new NearbyVenue { Name = "Al Fahidi Heritage Walk", Category = "Activity", IsIndoor = false, DistanceMinutes = 10 },
                new NearbyVenue
                {
                    Name = "Arabian Tea House",
                    Category = "Restaurant",
                    IsIndoor = false,
                    Setting = "mixed",
                    ShelterNote = "shaded courtyard with a fully air-conditioned indoor seating section",
                    DistanceMinutes = 11,
                },
            },
        };

        var pivots = CreatePivots(
            new RecordingChatNotifier(),
            new StubTravelContextProvider { Context = Context("Rain", 29, true) },
            venues,
            history,
            recording);

        await pivots.TryPublishPivotAsync(CancellationToken.None);

        Assert.Contains("Arabian Tea House", string.Join(" ", recording.PivotInput!.IndoorOptions));
    }

    [Fact]
    public async Task TheSameConflictIsNotRaisedTwice()
    {
        var notifier = new RecordingChatNotifier();
        var history = new StubChatHistoryService { FavoritesForToday = new[] { OutdoorSave("Al Fahidi Heritage Walk", "Activity · Outdoor · 10 min away") } };
        var pivots = CreatePivots(notifier, new StubTravelContextProvider { Context = Context("Rain", 29, true) }, history: history);

        Assert.True(await pivots.TryPublishPivotAsync(CancellationToken.None));
        Assert.False(await pivots.TryPublishPivotAsync(CancellationToken.None));
        Assert.Single(notifier.Published);
    }

    [Fact]
    public async Task TheAgentIsHandedShelterAndThePlaceItIsSaving()
    {
        var recording = new FakeSynthesizerAgent();
        var history = new StubChatHistoryService
        {
            FavoritesForToday = new[]
            {
                OutdoorSave("Al Fahidi Heritage Walk", "Activity · Outdoor · 10 min away"),
                new ChatMessageEntity { Id = 2, Content = "Top pick: Karak House.", Timestamp = SavedAt, IsFavorite = true },
            },
        };
        var venues = new StubNearbyVenueStore
        {
            Venues = new[]
            {
                new NearbyVenue { Name = "Al Fahidi Heritage Walk", Category = "Activity", IsIndoor = false, DistanceMinutes = 10 },
                new NearbyVenue { Name = "Karak House", Category = "Cafe", IsIndoor = true, DistanceMinutes = 6 },
                new NearbyVenue { Name = "Orfali Bros", Category = "Restaurant", IsIndoor = true, DistanceMinutes = 15 },
            },
        };

        var pivots = CreatePivots(
            new RecordingChatNotifier(),
            new StubTravelContextProvider { Context = Context("Rain", 29, true) },
            venues,
            history,
            recording);

        await pivots.TryPublishPivotAsync(CancellationToken.None);

        var pivot = recording.PivotInput;
        Assert.NotNull(pivot);
        Assert.Equal("Rain", pivot!.Condition);
        Assert.True(pivot.IsRaining);
        Assert.Contains("Al Fahidi Heritage Walk", pivot.SavedPlanSummary);
        // Only shelter is offered, and the outdoor stop it is replacing is never suggested.
        Assert.DoesNotContain("Al Fahidi Heritage Walk", string.Join(" ", pivot.IndoorOptions));
        Assert.Contains("Karak House", string.Join(" ", pivot.IndoorOptions));
    }

    [Fact]
    public async Task ADisabledCheckPublishesNothingAtAll()
    {
        var notifier = new RecordingChatNotifier();
        var history = new StubChatHistoryService { FavoritesForToday = new[] { OutdoorSave("Jumeirah Beach") } };

        var pivots = new WeatherPivotService(
            new StubScopeFactory(new StubServiceProvider(new Dictionary<Type, object>
            {
                [typeof(IChatHistoryService)] = history,
            })),
            notifier,
            new SignatureCooldown(TimeSpan.FromHours(4)),
            Microsoft.Extensions.Options.Options.Create(new WeatherPivotOptions { Enabled = false }),
            NullLogger<WeatherPivotService>.Instance);

        Assert.False(await pivots.TryPublishPivotAsync(CancellationToken.None));
        Assert.Empty(notifier.Published);
    }
}

public class SeverityTests
{
    [Theory]
    [InlineData("Rain", 29, true)]
    [InlineData("Thunderstorm", 24, false)]
    [InlineData("Sandstorm", 38, false)]
    [InlineData("Clear", 42, false)]
    [InlineData("Dust", 33, false)]
    public void SevereWeatherIsAnythingThatWouldSpoilThePlan(string condition, double temperatureC, bool raining)
    {
        Assert.True(Severity.Of(condition, raining, temperatureC).IsSevere);
    }

    [Theory]
    [InlineData("Clear", 31, false)]
    [InlineData("Clouds", 28, false)]
    [InlineData("Haze", 39, false)]
    public void ComfortableWeatherIsNotWorthAnInterruption(string condition, double temperatureC, bool raining)
    {
        Assert.False(Severity.Of(condition, raining, temperatureC).IsSevere);
    }
}

public class OutdoorPlanTests
{
    [Fact]
    public void AVenueTypedOutdoorNeedsSheltering()
    {
        var rich = new RichContent(new[]
        {
            new RichBlock(RichBlockKind.List, "Al Fahidi Heritage Walk", "Activity · Outdoor · 10 min away"),
        });

        Assert.True(OutdoorPlan.IsOutdoor(rich));
    }

    [Fact]
    public void AVenueTypedIndoorDoesNot()
    {
        var rich = new RichContent(new[]
        {
            new RichBlock(RichBlockKind.List, "Karak House", "Cafe · Indoor · 6 min away"),
        });

        Assert.False(OutdoorPlan.IsOutdoor(rich));
    }

    [Fact]
    public void AMixedVenueIsWorthAHeadsUp()
    {
        var rich = new RichContent(new[]
        {
            new RichBlock(RichBlockKind.List, "The Beach", "Restaurant · Mixed · 12 min away"),
        });

        Assert.True(OutdoorPlan.IsOutdoor(rich));
    }

    [Fact]
    public void ContentWithNoPlacesInItIsNotAPlan()
    {
        Assert.False(OutdoorPlan.IsOutdoor(RichContent.Empty));
        Assert.False(OutdoorPlan.IsOutdoor(null));
    }

    [Fact]
    public void AMessageWithNoBlocksFallsBackToTheTravelersOwnWords()
    {
        Assert.True(OutdoorPlan.MentionsOutdoor("There is a garden walk that stays cool until sunset."));
        Assert.False(OutdoorPlan.MentionsOutdoor("I will be indoors all afternoon."));
        Assert.False(OutdoorPlan.MentionsOutdoor(null));
    }
}

/// <summary>
/// The pivot has to see a bookmark the way the app actually stores it: through the real history
/// query against real SQLite. A stubbed service proves the agent talks to the notifier; this proves
/// a saved plan reaches it at all, which is the half that failed silently once already.
/// </summary>
public class WeatherPivotAgainstRealHistoryTests : IDisposable
{
    private readonly string _directory;
    private readonly string _databasePath;

    public WeatherPivotAgainstRealHistoryTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "meridian-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "chat.db");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Windows/CI can hold the file a moment longer; nothing to clean up after a test run.
        }
    }

    private DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_databasePath}").Options;

    [Fact]
    public async Task ThePivotSeesABookmarkThatWasSavedThroughTheRealService()
    {
        await using (var context = new TestDbContextFactory(Options()).CreateDbContext())
        {
            await context.Database.MigrateAsync();
            context.Profiles.Add(UserProfile.Default);
            await context.SaveChangesAsync();
        }

        var factory = new TestDbContextFactory(Options());
        var history = new ChatHistoryService(factory, NullLogger<ChatHistoryService>.Instance);

        var outdoor = await history.GetCurrentSessionAsync();

        var saved = await history.SaveMessageAsync(new ChatMessageEntity
        {
            SessionId = outdoor.Id,
            Role = "assistant",
            Content = "Top pick: Al Fahidi Heritage Walk.",
            Timestamp = DateTime.Now.AddHours(-1),
            RichContentJson = "{\"blocks\":[{\"kind\":3,\"text\":\"Al Fahidi Heritage Walk\",\"detail\":\"Activity · Outdoor · 10 min away\"}]}",
        });
        await history.ToggleFavoriteAsync(saved.Id);

        // The bookmark exists. The pivot still found nothing once, because the query it goes
        // through silently returned an empty list.
        Assert.Single(await history.GetFavoritesForCurrentSessionAsync());

        var notifier = new RecordingChatNotifier();
        var synthesizer = new FakeSynthesizerAgent();
        var pivots = new WeatherPivotService(
            new StubScopeFactory(new StubServiceProvider(new Dictionary<Type, object>
            {
                [typeof(IUserProfileService)] = new StubUserProfileService(),
                [typeof(ITravelContextProvider)] = new StubTravelContextProvider
                {
                    Context = new TravelContext
                    {
                        Location = "Downtown Dubai",
                        LocalTime = "20:45",
                        WeatherCondition = "Rain",
                        TemperatureC = 29,
                        IsRaining = true,
                    },
                },
                [typeof(IChatHistoryService)] = history,
                [typeof(INearbyVenueStore)] = new StubNearbyVenueStore(),
                [typeof(ISynthesizerAgent)] = synthesizer,
                [typeof(IProactiveSuggestionAgent)] = new FakeProactiveSuggestionAgent(),
                [typeof(IRulebookStore)] = new StubRulebookStore(),
            })),
            notifier,
            new SignatureCooldown(TimeSpan.FromHours(4)),
            Microsoft.Extensions.Options.Options.Create(new WeatherPivotOptions()),
            NullLogger<WeatherPivotService>.Instance);

        Assert.True(await pivots.TryPublishPivotAsync(CancellationToken.None));

        var published = Assert.Single(notifier.Published);
        Assert.Equal(IProactiveChatNotifier.Kinds.WeatherPivot, published.Kind);
        Assert.NotNull(synthesizer.PivotInput);
        Assert.Contains("Al Fahidi Heritage Walk", synthesizer.PivotInput!.SavedPlanSummary);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;

        public AppDbContext CreateDbContext() => new(_options);
    }
}

/// <summary>
/// The pivot is only useful if the scheduler actually calls it. It was injected and never invoked
/// once, which no unit test could catch because each half looked correct on its own.
/// </summary>
public class WeatherPivotSchedulingTests
{
    [Fact]
    public async Task TheSchedulerRunsThePivotBeforeTheSuggestionCycle()
    {
        var notifier = new RecordingChatNotifier();
        var activity = new FakeChatActivityTracker { IsActiveResult = true };

        // A rainy context over a saved outdoor plan: the pivot has everything it needs to fire.
        var history = new StubChatHistoryService
        {
            FavoritesForToday = new[]
            {
                new ChatMessageEntity
                {
                    Id = 1,
                    Content = "Top pick: Al Fahidi Heritage Walk.",
                    Timestamp = DateTime.Now.AddHours(-1),
                    IsFavorite = true,
                    RichContentJson = "{\"blocks\":[{\"kind\":3,\"text\":\"Al Fahidi Heritage Walk\",\"detail\":\"Activity · Outdoor · 10 min away\"}]}",
                },
            },
        };
        var context = new StubTravelContextProvider
        {
            Context = new TravelContext
            {
                Location = "Downtown Dubai",
                LocalTime = "20:45",
                WeatherCondition = "Rain",
                TemperatureC = 29,
                IsRaining = true,
            },
        };

        var services = new Dictionary<Type, object>
        {
            [typeof(IProactiveSuggestionAgent)] = new FakeProactiveSuggestionAgent(),
            [typeof(ISynthesizerAgent)] = new FakeSynthesizerAgent(),
            [typeof(IUserProfileService)] = new StubUserProfileService(),
            [typeof(IRulebookStore)] = new StubRulebookStore(),
            [typeof(INearbyVenueStore)] = new StubNearbyVenueStore(),
            [typeof(ITravelContextProvider)] = context,
            [typeof(IChatHistoryService)] = history,
        };

        var pivots = new WeatherPivotService(
            new StubScopeFactory(new StubServiceProvider(services)),
            notifier,
            new SignatureCooldown(TimeSpan.FromHours(4)),
            Microsoft.Extensions.Options.Options.Create(new WeatherPivotOptions()),
            NullLogger<WeatherPivotService>.Instance);

        var scheduler = new ProactiveSuggestionScheduler(
            new StubScopeFactory(new StubServiceProvider(services)),
            notifier,
            activity,
            new SignatureCooldown(TimeSpan.FromHours(4)),
            pivots,
            Microsoft.Extensions.Options.Options.Create(new ProactiveSuggestionsOptions()),
            NullLogger<ProactiveSuggestionScheduler>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await scheduler.StartAsync(cts.Token);
        await notifier.WaitForPushAsync(TimeSpan.FromSeconds(5));

        var published = Assert.Single(notifier.Published);
        Assert.Equal(IProactiveChatNotifier.Kinds.WeatherPivot, published.Kind);
    }

    [Fact]
    public async Task ThePivotPreemptsTheSuggestionCycleWhenItFires()
    {
        var suggestionAgent = new FakeProactiveSuggestionAgent();
        var notifier = new RecordingChatNotifier();

        var services = new Dictionary<Type, object>
        {
            [typeof(IProactiveSuggestionAgent)] = suggestionAgent,
            [typeof(ISynthesizerAgent)] = new FakeSynthesizerAgent(),
            [typeof(IUserProfileService)] = new StubUserProfileService(),
            [typeof(IRulebookStore)] = new StubRulebookStore(),
            [typeof(INearbyVenueStore)] = new StubNearbyVenueStore(),
            [typeof(ITravelContextProvider)] = new StubTravelContextProvider
            {
                Context = new TravelContext
                {
                    Location = "Downtown Dubai",
                    WeatherCondition = "Thunderstorm",
                    TemperatureC = 24,
                    IsRaining = true,
                },
            },
            [typeof(IChatHistoryService)] = new StubChatHistoryService
            {
                FavoritesForToday = new[]
                {
                    new ChatMessageEntity
                    {
                        Id = 1,
                        Content = "Top pick: Jumeirah Beach.",
                        Timestamp = DateTime.Now.AddHours(-1),
                        IsFavorite = true,
                        RichContentJson = "{\"blocks\":[{\"kind\":3,\"text\":\"Jumeirah Beach\",\"detail\":\"Activity · Outdoor · 10 min away\"}]}",
                    },
                },
            },
        };

        var pivots = new WeatherPivotService(
            new StubScopeFactory(new StubServiceProvider(services)),
            notifier,
            new SignatureCooldown(TimeSpan.FromHours(4)),
            Microsoft.Extensions.Options.Options.Create(new WeatherPivotOptions()),
            NullLogger<WeatherPivotService>.Instance);

        var scheduler = new ProactiveSuggestionScheduler(
            new StubScopeFactory(new StubServiceProvider(services)),
            notifier,
            new FakeChatActivityTracker { IsActiveResult = true },
            new SignatureCooldown(TimeSpan.FromHours(4)),
            pivots,
            Microsoft.Extensions.Options.Options.Create(new ProactiveSuggestionsOptions()),
            NullLogger<ProactiveSuggestionScheduler>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await scheduler.StartAsync(cts.Token);
        await notifier.WaitForPushAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(IProactiveChatNotifier.Kinds.WeatherPivot, Assert.Single(notifier.Published).Kind);

        // The point of pre-empting: the suggestion agent is never asked at all.
        Assert.Empty(suggestionAgent.Requests);
    }
}
