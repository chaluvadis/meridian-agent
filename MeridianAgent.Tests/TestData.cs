namespace MeridianAgent.Tests;

/// <summary>Shared fixtures: a traveler in Dubai at 20:45 on a warm clear evening.</summary>
public static class TestData
{
    public static UserProfile Profile => new()
    {
        Id = "default",
        DisplayName = "Aria",
        Destination = "Dubai, UAE",
        TravelerProfile = TravelerProfile.FirstTime,
        GroupComposition = GroupComposition.Couple,
        DietaryRules = new[] { "vegetarian" },
        Interests = new[] { "old town souks", "rooftop cafés" },
        AvoidList = new[] { "crowded mega malls" },
        Pacing = PacingPreference.Moderate,
        Budget = BudgetRange.Medium,
        AttireNote = "light scarf in bag",
        HomeBase = "Downtown Dubai",
    };

    public static TravelContext Context => new()
    {
        Location = "Dubai, UAE",
        LocalTime = "20:45",
        TemperatureC = 31,
        WeatherCondition = "Clear",
        IsRaining = false,
        WeatherSummary = "Warm, humid evening, 31°C",
    };

    public static IReadOnlyList<NearbyVenue> NearbyVenues => new[]
    {
        new NearbyVenue
        {
            Name = "Karak House",
            Category = "cafe",
            IsOpenNow = true,
            IsIndoor = true,
            DietaryOptions = new[] { "vegetarian" },
            DistanceMinutes = 6,
        },
        new NearbyVenue
        {
            Name = "Al Fahidi Heritage Walk",
            Category = "activity",
            IsOpenNow = true,
            IsIndoor = false,
            DistanceMinutes = 12,
        },
        new NearbyVenue
        {
            Name = "Dubai Mall Food Court",
            Category = "restaurant",
            IsOpenNow = false,
            IsIndoor = false,
            DietaryOptions = new[] { "vegetarian" },
            DistanceMinutes = 9,
        },
    };

    public static DestinationRulebook Rulebook => new()
    {
        Destination = "Dubai, UAE",
        Rules = new[]
        {
            new SafeguardRule
            {
                RuleId = "dubai-photo-1",
                Category = RuleCategory.Photography,
                Rule = "Photographing local people, particularly women, without permission is an offence.",
                Severity = RiskSeverity.Medium,
                GentleFix = "Keep phones pocketed inside the hall; the veranda is fine for photos.",
            },
            new SafeguardRule
            {
                RuleId = "kyoto-dress-1",
                Category = RuleCategory.DressCode,
                Rule = "Shoulders covered at major temples.",
                Severity = RiskSeverity.Low,
                GentleFix = "A light scarf handles it.",
            },
        },
    };

    public static TravelRequest Request(string query = "We're exhausted - anywhere good for dinner?") => new(
        query,
        Profile,
        Context,
        NearbyVenues,
        Rulebook);

    public static SynthesisRequest SynthesisRequest(
        string query = "We're exhausted - anywhere good for dinner?",
        string travelerName = "Aria") => new(
            query,
            Itinerary,
            SafeguardReport.Cleared,
            travelerName);

    public static ExtractedConstraints Constraints => new()
    {
        QueryIntent = QueryIntent.Food,
        RelevantDietary = new[] { "vegetarian" },
        RelevantInterests = new[] { "temples", "quiet cafés" },
        AvoidList = new[] { "crowded nightclubs" },
        PacingPreference = PacingPreference.Slow,
        EnergyLevel = EnergyLevel.Low,
        TimeSensitivity = TimeSensitivity.Normal,
        BudgetRange = BudgetRange.Medium,
    };

    public static ProposedItinerary Itinerary => new(new[]
    {
        new Venue
        {
            Name = "Karak House",
            VenueType = VenueType.Indoor,
            Category = VenueCategory.Cafe,
            TravelTimeMinutes = 6,
            WhyItFits = "Warm, quiet, fully vegetarian - a six minute walk on a rainy night.",
            EstimatedDuration = "45-60 minutes",
            BestTimeToVisit = "Tonight, before it gets busy",
        },
    });

    public static SafeguardReport FlaggedReport => new(
        SafeguardStatus.Flagged,
        new[]
        {
            new RiskWarning
            {
                AppliesToVenue = "Karak House",
                Severity = RiskSeverity.Low,
                RuleCategory = RuleCategory.DressCode,
                RiskDescription = "Shoulders covered at major temples.",
                GentleFix = "A light scarf handles it.",
            },
        });
}

public sealed class FakeContextAgent : IContextAgent
{
    public List<TravelRequest> Requests { get; } = new();

    public ExtractedConstraints Result { get; set; } = TestData.Constraints;

    public Exception? Error { get; set; }

    public Task<ExtractedConstraints> ExtractAsync(TravelRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (Error is not null)
        {
            throw Error;
        }

        return Task.FromResult(Result);
    }
}

public sealed class FakeSearchAgent : ISearchAgent
{
    public List<TravelRequest> Requests { get; } = new();

    public List<ExtractedConstraints> ConstraintsSeen { get; } = new();

    public ProposedItinerary Result { get; set; } = TestData.Itinerary;

    /// <summary>Number of initial calls that fail with NoVenuesFound before <see cref="Result"/> is returned.</summary>
    public int NoVenuesFailures { get; set; }

    public string NoVenuesReason { get; set; } = "nothing fits";

    public Exception? Error { get; set; }

    public Task<ProposedItinerary> ProposeAsync(
        ExtractedConstraints constraints,
        TravelRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        ConstraintsSeen.Add(constraints);

        if (NoVenuesFailures > 0)
        {
            NoVenuesFailures--;
            throw new NoVenuesFoundException(NoVenuesReason);
        }

        if (Error is not null)
        {
            throw Error;
        }

        return Task.FromResult(Result);
    }
}

public sealed class FakeSafeguardAgent : ISafeguardAgent
{
    public SafeguardReport Result { get; set; } = SafeguardReport.Cleared;

    public Exception? Error { get; set; }

    public Task<SafeguardReport> CheckAsync(
        ProposedItinerary plan,
        TravelRequest request,
        CancellationToken cancellationToken = default)
    {
        if (Error is not null)
        {
            throw Error;
        }

        return Task.FromResult(Result);
    }
}

public sealed class FakeSynthesizerAgent : ISynthesizerAgent
{
    public List<SynthesisRequest> Requests { get; } = new();

    public string[] Chunks { get; set; } = new[] { "Top ", "pick: ", "Karak House." };

    /// <summary>Text <see cref="GenerateItineraryAsync" /> returns.</summary>
    public string ItineraryResult { get; set; } = "Day 1 in Dubai.\n\n12:00 — Karak House.";

    /// <summary>Text <see cref="ExecuteWeatherPivotAsync" /> returns.</summary>
    public string PivotResult { get; set; } = "Heads up: rain is starting at 16:00. This will affect your rooftop plan.";

    /// <summary>What was last asked to be compiled, or null when it was never called.</summary>
    public List<ChatMessage>? ItineraryInput { get; private set; }

    /// <summary>What the pivot was told about the weather, or null when it was never called.</summary>
    public WeatherPivot? PivotInput { get; private set; }

    public IAsyncEnumerable<string> StreamAsync(SynthesisRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Stream();

        async IAsyncEnumerable<string> Stream()
        {
            foreach (var chunk in Chunks)
            {
                await Task.Yield();
                yield return chunk;
            }
        }
    }

    public async Task<string> CompleteAsync(SynthesisRequest request, CancellationToken cancellationToken = default)
    {
        var builder = new System.Text.StringBuilder();
        await foreach (var chunk in StreamAsync(request, cancellationToken).ConfigureAwait(false))
        {
            builder.Append(chunk);
        }

        return builder.ToString();
    }

    public List<(IReadOnlyList<ProactiveSuggestion> Suggestions, ProactiveRequest Request)> ProactiveRequests { get; } = new();

    public string ProactiveResponse { get; set; } = "It's cooling down — Karak House is six minutes away.";

    public Exception? ProactiveError { get; set; }

    public List<UserProfile> WelcomeRequests { get; } = new();

    public string WelcomeResponse { get; set; } = "Welcome, Aria! A balanced traveler who loves old town souks — I'll keep the mega malls off your list.";

    public Exception? WelcomeError { get; set; }

    public async Task<string> ExecuteProactiveAsync(
        IReadOnlyList<ProactiveSuggestion> suggestions,
        ProactiveRequest request,
        CancellationToken cancellationToken = default)
    {
        ProactiveRequests.Add((suggestions, request));
        if (ProactiveError is not null)
        {
            throw ProactiveError;
        }

        await Task.Yield();
        return ProactiveResponse;
    }

    public async Task<string> ExecuteWelcomeAsync(UserProfile profile, CancellationToken cancellationToken = default)
    {
        WelcomeRequests.Add(profile);
        if (WelcomeError is not null)
        {
            throw WelcomeError;
        }

        await Task.Yield();
        return WelcomeResponse;
    }

    /// <summary>Structured reply (message plus quick actions) returned by <see cref="SynthesizeAsync"/>.</summary>
    public SynthesisResponse SynthesisResult { get; set; } = new(
        "Top pick: Karak House.",
        new[]
        {
            new QuickAction("📌 Show on Map", "Karak House", QuickActionType.Navigate),
            new QuickAction("✅ Accept & Plan", "Plan an evening around Karak House", QuickActionType.AcceptSuggestion),
            new QuickAction("🔄 Find Alternatives", "somewhere calmer than Karak House", QuickActionType.RefineSearch),
        });

    public Exception? SynthesisError { get; set; }

    public async Task<SynthesisResponse> SynthesizeAsync(
        SynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        if (SynthesisError is not null)
        {
            throw SynthesisError;
        }

        await Task.Yield();
        return SynthesisResult;
    }

    public Task<string> GenerateItineraryAsync(
        UserProfile profile,
        IReadOnlyList<ChatMessage> savedMessages,
        CancellationToken cancellationToken = default)
    {
        ItineraryInput = savedMessages.ToList();
        return Task.FromResult(ItineraryResult);
    }

    public Task<string> ExecuteWeatherPivotAsync(WeatherPivot pivot, CancellationToken cancellationToken = default)
    {
        PivotInput = pivot;
        return Task.FromResult(PivotResult);
    }
}

public sealed class FakeProactiveSuggestionAgent : IProactiveSuggestionAgent
{
    public List<ProactiveRequest> Requests { get; } = new();

    public IReadOnlyList<ProactiveSuggestion> Result { get; set; } = new[]
    {
        new ProactiveSuggestion(
            "a quiet rooftop before it gets busy",
            "the heat is easing and Karak House is six minutes away",
            0.82,
            "in the next hour",
            "cafe",
            "rooftop cafés"),
        new ProactiveSuggestion(
            "an early souk stroll",
            "Al Fahidi is at its calmest right after sunset",
            0.61,
            "tonight",
            "activity",
            "old town souks"),
        new ProactiveSuggestion(
            "a food court escape",
            "air-conditioned and vegetarian, but it is a mega mall",
            0.4,
            "later",
            "restaurant",
            null),
    };

    public Exception? Error { get; set; }

    public Task<IReadOnlyList<ProactiveSuggestion>> GenerateAsync(
        ProactiveRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (Error is not null)
        {
            throw Error;
        }

        return Task.FromResult(Result);
    }
}

public sealed class RecordingChatNotifier : IProactiveChatNotifier
{
    private readonly TaskCompletionSource _pushed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<(string MessageId, string Content, string Kind)> Published { get; } = new();

    public Task PublishAsync(string messageId, string content, CancellationToken cancellationToken = default)
    {
        return PublishAsync(messageId, content, IProactiveChatNotifier.Kinds.Suggestion, cancellationToken);
    }

    public Task PublishAsync(string messageId, string content, string kind, CancellationToken cancellationToken = default)
    {
        Published.Add((messageId, content, kind));
        _pushed.TrySetResult();
        return Task.CompletedTask;
    }

    public async Task WaitForPushAsync(TimeSpan timeout) =>
        await _pushed.Task.WaitAsync(timeout).ConfigureAwait(false);
}

public sealed class FakeChatActivityTracker : IChatActivityTracker
{
    public bool IsActiveResult { get; set; } = true;

    public List<TimeSpan> Windows { get; } = new();

    public DateTime LastActivityUtc { get; private set; } = DateTime.MinValue;

    public int RecordCount { get; private set; }

    public void RecordActivity()
    {
        LastActivityUtc = DateTime.UtcNow;
        RecordCount++;
    }

    public bool IsActive(TimeSpan window)
    {
        Windows.Add(window);
        return IsActiveResult;
    }
}

public sealed class StubUserProfileStore : IUserProfileStore
{
    public UserProfile Profile { get; set; } = TestData.Profile;

    public Task<UserProfile?> GetAsync(string profileId, CancellationToken cancellationToken = default) =>
        Task.FromResult<UserProfile?>(Profile);

    public Task SaveAsync(UserProfile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<UserProfile>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserProfile>>(new[] { Profile });
}

/// <summary>In-memory profile service used by the scheduler tests (SQLite lives in the infra tests).</summary>
public sealed class StubUserProfileService : IUserProfileService
{
    public UserProfile? Profile { get; set; } = TestData.Profile;

    public bool HasCompletedProfile { get; set; } = true;

    public int SaveCount { get; private set; }

    public int MarkWelcomeSeenCount { get; private set; }

    public Task<UserProfile?> GetProfileAsync(string profileId = IUserProfileService.DefaultProfileId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Profile);

    public Task<UserProfile> GetOrCreateAsync(string profileId = IUserProfileService.DefaultProfileId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Profile ?? UserProfile.Default with { Id = profileId });

    public Task<bool> HasCompletedProfileAsync(string profileId = IUserProfileService.DefaultProfileId, CancellationToken cancellationToken = default) =>
        Task.FromResult(HasCompletedProfile);

    public Task SaveProfileAsync(UserProfile profile, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        Profile = profile;
        return Task.CompletedTask;
    }

    public Task MarkWelcomeSeenAsync(string profileId = IUserProfileService.DefaultProfileId, CancellationToken cancellationToken = default)
    {
        MarkWelcomeSeenCount++;
        Profile = Profile is null ? null : Profile with { PendingWelcome = false };
        return Task.CompletedTask;
    }
}

public sealed class StubRulebookStore : IRulebookStore
{
    public DestinationRulebook? Rulebook { get; set; } = TestData.Rulebook;

    public Task<DestinationRulebook?> GetAsync(string destination, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rulebook);

    public Task SaveAsync(DestinationRulebook rulebook, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<DestinationRulebook>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DestinationRulebook>>(
            Rulebook is null ? Array.Empty<DestinationRulebook>() : new[] { Rulebook });
}

public sealed class StubNearbyVenueStore : INearbyVenueStore
{
    public IReadOnlyList<NearbyVenue> Venues { get; set; } = TestData.NearbyVenues;

    public Task<IReadOnlyList<NearbyVenue>> GetAsync(string location, CancellationToken cancellationToken = default) =>
        Task.FromResult(Venues);
}

public sealed class StubTravelContextProvider : ITravelContextProvider
{
    public TravelContext Context { get; set; } = TestData.Context;

    public Exception? Error { get; set; }

    public Task<TravelContext> GetCurrentAsync(string location, CancellationToken cancellationToken = default)
    {
        if (Error is not null)
        {
            throw Error;
        }

        return Task.FromResult(Context);
    }
}

/// <summary>
/// Stands in for the history store when a test only needs to say what is bookmarked today. Only
/// the members the pivot check reads are wired to anything; the rest are unreachable by design.
/// </summary>
public sealed class StubChatHistoryService : IChatHistoryService
{
    public IReadOnlyList<ChatMessageEntity> FavoritesForToday { get; set; } = Array.Empty<ChatMessageEntity>();

    public IReadOnlyList<ChatMessageEntity> Favorites { get; set; } = Array.Empty<ChatMessageEntity>();

    public IReadOnlyList<ChatMessageEntity> SessionMessages { get; set; } = Array.Empty<ChatMessageEntity>();

    public ChatSession Session { get; set; } = new() { Date = DateOnly.FromDateTime(DateTime.Now), DayNumber = 1 };

    public int SavedCount { get; private set; }

    public Task<ChatSession> GetCurrentSessionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Session);

    public Task<ChatMessageEntity> SaveMessageAsync(ChatMessageEntity message, CancellationToken cancellationToken = default)
    {
        SavedCount++;
        message.Id = SavedCount;
        return Task.FromResult(message);
    }

    public Task<bool?> ToggleFavoriteAsync(int messageId, CancellationToken cancellationToken = default) => Task.FromResult<bool?>(null);

    public Task<IReadOnlyList<ChatMessageEntity>> GetFavoritesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Favorites);

    public Task<IReadOnlyList<ChatMessageEntity>> GetFavoritesForCurrentSessionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(FavoritesForToday);

    public Task<IReadOnlyList<ChatSession>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ChatSession>>(new[] { Session });

    public Task<IReadOnlyList<ChatMessageEntity>> GetMessagesForSessionAsync(int sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(SessionMessages);
}
