namespace MeridianAgent.Tests;

using MeridianAgent.Domain.Entities;

using MeridianAgent.Infrastructure.AI;
using MeridianAgent.Infrastructure.AI.Agents;

/// <summary>
/// The Search Agent negotiates instead of refusing: when a soft constraint cannot be met it says so
/// in "compromise" rather than returning an empty plan, and it only refuses when a hard constraint
/// (dietary, avoid list) or the venue pool itself makes the request impossible.
/// </summary>
public class SearchAgentNegotiationTests
{
    private static SearchAgent CreateAgent(params string[] responses) =>
        new(new ScriptedLlmClient(responses), NullLogger<SearchAgent>.Instance);

    [Fact]
    public async Task ProposeAsync_CarriesTheConcessionTheAgentDeclared()
    {
        var agent = CreateAgent("""
            [
              {
                "venue_name": "Zuma",
                "venue_type": "indoor",
                "category": "restaurant",
                "travel_time_minutes": 15,
                "why_it_fits": "The quietest table nearby with a full vegetarian tasting menu.",
                "compromise": "It is 15 mins away instead of your preferred 10 mins",
                "estimated_duration": "90 minutes",
                "best_time_to_visit": "Tonight"
              }
            ]
            """);

        var itinerary = await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        var venue = Assert.Single(itinerary.Venues);
        Assert.Equal("Zuma", venue.Name);
        Assert.Equal("It is 15 mins away instead of your preferred 10 mins", venue.Compromise);
    }

    [Fact]
    public async Task ProposeAsync_TreatsAnAbsentConcessionAsAnExactMatch()
    {
        var agent = CreateAgent("""
            [{"venue_name": "Karak House", "venue_type": "indoor", "category": "cafe",
              "why_it_fits": "Exact match.", "compromise": null}]
            """);

        var itinerary = await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        Assert.Null(Assert.Single(itinerary.Venues).Compromise);
    }

    [Fact]
    public async Task ProposeAsync_IgnoresABlankConcession()
    {
        var agent = CreateAgent("""
            [{"venue_name": "Karak House", "venue_type": "indoor", "category": "cafe",
              "why_it_fits": "Exact match.", "compromise": "   "}]
            """);

        var itinerary = await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        Assert.Null(Assert.Single(itinerary.Venues).Compromise);
    }

    [Fact]
    public async Task ProposeAsync_NegotiatesRatherThanRefusingOnASoftConstraintMiss()
    {
        // The old prompt returned an empty array here and the traveler was told nothing fits.
        var agent = CreateAgent("""
            [{"venue_name": "Orfali Bros", "venue_type": "indoor", "category": "restaurant",
              "travel_time_minutes": 18,
              "why_it_fits": "Closest thing to the vibe you asked for that is still vegetarian.",
              "compromise": "It is 8 mins further out than your usual limit, but it is the only quiet option"}]
            """);

        var itinerary = await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        var venue = Assert.Single(itinerary.Venues);
        Assert.Equal("Orfali Bros", venue.Name);
        Assert.Contains("further out", venue.Compromise!);
    }

    [Fact]
    public async Task ProposeAsync_StillRefusesWhenAHardConstraintCannotBeMet()
    {
        var agent = CreateAgent("""{"error": true, "reason": "You need vegetarian and quiet, but everything nearby is loud, meat-focused street food"}""");

        var exception = await Assert.ThrowsAsync<NoVenuesFoundException>(() =>
            agent.ProposeAsync(TestData.Constraints, TestData.Request()));

        Assert.Contains("vegetarian and quiet", exception.Reason);
    }

    [Fact]
    public async Task ProposeAsync_StillRefusesAnEmptyArray()
    {
        var agent = CreateAgent("""[]""");

        await Assert.ThrowsAsync<NoVenuesFoundException>(() =>
            agent.ProposeAsync(TestData.Constraints, TestData.Request()));
    }

    [Fact]
    public async Task ProposeAsync_AsksForNearbyVenuesWhenNoneWereSupplied()
    {
        var client = new ScriptedLlmClient(
            """[{"venue_name": "Karak House", "venue_type": "indoor", "category": "cafe"}]""");
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance);

        await agent.ProposeAsync(TestData.Constraints, EmptyRequest());

        var prompt = client.Calls[0].Prompt;
        Assert.Contains("nearby_venues list above is empty", prompt);
        Assert.Contains("general knowledge", prompt);
        Assert.Contains("\"nearby_venues\": []", prompt);
    }

    [Fact]
    public async Task ProposeAsync_DoesNotAskForNearbyVenuesWhenTheListHasEntries()
    {
        var client = new ScriptedLlmClient(
            """[{"venue_name": "Karak House", "venue_type": "indoor", "category": "cafe"}]""");
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance);

        await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        Assert.DoesNotContain("general knowledge", client.Calls[0].Prompt);
    }

    [Fact]
    public async Task ProposeAsync_TellsTheModelToNegotiateRatherThanRefuse()
    {
        var client = new ScriptedLlmClient(
            """[{"venue_name": "Karak House", "venue_type": "indoor", "category": "cafe"}]""");
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance);

        await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        var prompt = client.Calls[0].Prompt;
        Assert.Contains("NEGOTIATE, DO NOT REFUSE", prompt);
        Assert.Contains("CLOSEST POSSIBLE MATCH", prompt);
        Assert.Contains("HARD constraints are absolute", prompt);
        Assert.Contains("preferences, not filters", prompt);
    }

    private static TravelRequest EmptyRequest() => TestData.Request() with
    {
        NearbyVenues = Array.Empty<NearbyVenue>(),
    };
}

/// <summary>
/// A negotiated plan has to stay visible to the model that speaks to the traveler, so the
/// compromise is serialized into the itinerary the Synthesizer is handed rather than dropped here.
/// </summary>
public class CompromiseHandoffTests
{
    [Fact]
    public void ItineraryCarriesTheCompromiseIntoTheSynthesizerJson()
    {
        var compromised = new ProposedItinerary(new[]
        {
            new Venue
            {
                Name = "Zuma",
                VenueType = VenueType.Indoor,
                Category = VenueCategory.Restaurant,
                TravelTimeMinutes = 15,
                WhyItFits = "Quiet and fully vegetarian.",
                Compromise = "It is 15 mins away instead of your preferred 10 mins",
            },
        });

        var json = JsonElementReader.Serialize(compromised.Venues);

        Assert.Contains("\"compromise\"", json);
        Assert.Contains("instead of your preferred 10 mins", json);
    }

    [Fact]
    public void AnExactMatchSendsNoCompromiseFieldAtAll()
    {
        var exact = new ProposedItinerary(new[]
        {
            new Venue
            {
                Name = "Karak House",
                VenueType = VenueType.Indoor,
                Category = VenueCategory.Cafe,
                TravelTimeMinutes = 6,
                WhyItFits = "Warm, quiet, fully vegetarian.",
            },
        });

        var json = JsonElementReader.Serialize(exact.Venues);

        Assert.DoesNotContain("compromise", json);
    }
}

/// <summary>
/// The Synthesizer is the only voice the traveler hears, so both of its prompts have to know how to
/// present a negotiated plan. Asserted against the prompt text itself, which is where those rules live.
/// </summary>
public class SynthesizerCompromisePromptTests
{
    private static readonly string[] Prompts =
    {
        typeof(SynthesizerAgent).GetField("Prompt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null) as string ?? string.Empty,
        typeof(SynthesizerAgent).GetField("QuickActionsPrompt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null) as string ?? string.Empty,
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void BothPromptsTellTheVoiceHowToPresentACompromise(int index)
    {
        var prompt = Prompts[index];

        Assert.Contains("compromise", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("apolog", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("safeguard", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheStructuredPromptAlsoOffersToLookForSomethingCloser()
    {
        // The chat uses the structured prompt: a compromise should hand the traveler a way out.
        var structured = Prompts[1];

        Assert.Contains("refine_search", structured);
        Assert.Contains("compromise", structured);
    }
}

/// <summary>
/// A small model asked for an array often answers with a single object. Rejecting that means a
/// usable venue never reaches the traveler, which is the same dead end the negotiator removes.
/// </summary>
public class SearchAgentShapeTests
{
    private static SearchAgent CreateAgent(params string[] responses) =>
        new(new ScriptedLlmClient(responses), NullLogger<SearchAgent>.Instance);

    [Fact]
    public async Task ProposeAsync_AcceptsASingleVenueObjectInsteadOfAnArray()
    {
        var agent = CreateAgent("""
            {
              "venue_name": "Orfali Bros",
              "venue_type": "indoor",
              "category": "restaurant",
              "travel_time_minutes": 15,
              "why_it_fits": "The quietest vegetarian table nearby.",
              "compromise": "It is 5 mins further out than your usual limit"
            }
            """);

        var itinerary = await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        var venue = Assert.Single(itinerary.Venues);
        Assert.Equal("Orfali Bros", venue.Name);
        Assert.Equal(15, venue.TravelTimeMinutes);
        Assert.Contains("further out", venue.Compromise!);
    }

    [Fact]
    public async Task ProposeAsync_CarriesTheRawResponseInTheFailureForTheLogs()
    {
        // An agent failure reaches the orchestrator's catch, which logs the exception message: the
        // last raw response rides along so a production log shows what the model actually said.
        // Each unparseable answer costs an extra call to the JSON sanitizer, so four responses cover
        // the two attempts.
        var client = new ScriptedLlmClient(
            "not json at all",
            "still not json",
            "gave up entirely",
            "not repairable");
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance);

        var exception = await Assert.ThrowsAsync<Domain.Exceptions.AgentInvocationException>(() =>
            agent.ProposeAsync(TestData.Constraints, TestData.Request()));

        // The last raw response from the main model, not the sanitizer's: it is what the model said
        // immediately before the agent gave up.
        Assert.Contains("gave up entirely", exception.Message);
        Assert.Contains("last raw response", exception.Message);
    }

    [Fact]
    public async Task ProposeAsync_StillRejectsAnArrayOfNonObjects()
    {
        var agent = CreateAgent("""["Karak House", "Orfali Bros"]""", """["Karak House", "Orfali Bros"]""");

        await Assert.ThrowsAsync<AgentInvocationException>(() =>
            agent.ProposeAsync(TestData.Constraints, TestData.Request()));
    }

    [Fact]
    public async Task ProposeAsync_StillRejectsSomethingThatIsNotVenuesAtAll()
    {
        var agent = CreateAgent("""{"complaint": "there is nowhere good to eat"}""", """{"complaint": "there is nowhere good to eat"}""");

        await Assert.ThrowsAsync<AgentInvocationException>(() =>
            agent.ProposeAsync(TestData.Constraints, TestData.Request()));
    }

    [Fact]
    public async Task ProposeAsync_KeepsTreatingAnEmptyArrayAsNoVenues()
    {
        var agent = CreateAgent("""[]""");

        await Assert.ThrowsAsync<NoVenuesFoundException>(() =>
            agent.ProposeAsync(TestData.Constraints, TestData.Request()));
    }
}

/// <summary>
/// A compromise the traveler cannot weigh — "slightly further out" — is a dead end wearing a
/// trade, so it is rejected and the agent is told why. The venue itself is never the price of that
/// rule: a last attempt that still hedges is accepted rather than losing the plan.
/// </summary>
public class CompromiseConsistencyTests
{
    private static SearchAgent CreateAgent(params string[] responses) =>
        new(new ScriptedLlmClient(responses), NullLogger<SearchAgent>.Instance);

    private static string Venue(string compromise) => $$"""
        [{"venue_name": "Orfali Bros", "venue_type": "indoor", "category": "restaurant",
          "travel_time_minutes": 15,
          "why_it_fits": "The quietest vegetarian table nearby.",
          "compromise": "{{compromise}}"}]
        """;

    [Fact]
    public async Task ProposeAsync_RetriesWhenTheCompromiseIsOnlyAHedge()
    {
        var client = new ScriptedLlmClient(Venue("slightly further out"), Venue("It is 15 mins away instead of your preferred 10 mins"));
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance);

        var itinerary = await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        Assert.Equal(2, client.Calls.Count);
        Assert.Contains("compromise must state the trade", client.Calls[1].Prompt);
        Assert.Equal("It is 15 mins away instead of your preferred 10 mins", Assert.Single(itinerary.Venues).Compromise);
    }

    [Fact]
    public async Task ProposeAsync_KeepsTheVenueWhenEveryAttemptHedges()
    {
        // The venue is the valuable part: losing the whole plan to a wording rule is the worse trade.
        var client = new ScriptedLlmClient(Venue("slightly further out"), Venue("still vaguely out of the way"));
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance);

        var itinerary = await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        Assert.Equal(2, client.Calls.Count);
        var venue = Assert.Single(itinerary.Venues);
        Assert.Equal("Orfali Bros", venue.Name);
        Assert.Equal("still vaguely out of the way", venue.Compromise);
    }

    [Fact]
    public async Task ProposeAsync_LeavesAnExactMatchAloneOnEveryAttempt()
    {
        var client = new ScriptedLlmClient(
            """[{"venue_name": "Karak House", "venue_type": "indoor", "category": "cafe", "compromise": null}]""",
            """[{"venue_name": "Karak House", "venue_type": "indoor", "category": "cafe", "compromise": null}]""");
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance);

        var itinerary = await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        Assert.Single(client.Calls);
        Assert.Null(Assert.Single(itinerary.Venues).Compromise);
    }

    [Theory]
    [InlineData("It is 15 mins away instead of your preferred 10 mins")]
    [InlineData("Italian rather than the Japanese you asked for")]
    [InlineData("about 20 dirhams more per head than you had in mind")]
    [InlineData("It is 8 min further out than your usual limit, but it is the only quiet table")]
    public async Task ProposeAsync_KeepsAMeasuredOrNamedTradeOnTheFirstAttempt(string compromise)
    {
        var client = new ScriptedLlmClient(Venue(compromise), Venue(compromise));
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance);

        var itinerary = await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        // Accepted as written, so the agent never had to be told twice.
        Assert.Single(client.Calls);
        Assert.Equal(compromise, Assert.Single(itinerary.Venues).Compromise);
    }

    [Theory]
    [InlineData("slightly further out than you would like")]
    [InlineData("a bit pricier")]
    [InlineData("close enough to what you wanted")]
    [InlineData("not the vibe you had in mind")]
    public async Task ProposeAsync_WontPassOffAHedgeAsATrade(string hedge)
    {
        var client = new ScriptedLlmClient(Venue(hedge), Venue("It is 15 mins away instead of your preferred 10 mins"));
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance);

        var itinerary = await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        Assert.Equal(2, client.Calls.Count);
        Assert.Contains(hedge, client.Calls[1].Prompt);
        Assert.Equal("It is 15 mins away instead of your preferred 10 mins", Assert.Single(itinerary.Venues).Compromise);
    }
}

/// <summary>
/// The itinerary is compiled from what the traveler saved, not from what the agent thinks would
/// fill a day. Empty saves and empty answers both have to come back as "nothing", because a model
/// asked to plan an empty day will happily invent one.
/// </summary>
public class ItineraryExportTests
{
    private static readonly DateTime Stamp = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Local);

    private static ChatMessage Saved(string content) =>
        new("saved-1", "assistant", content, Stamp, MessageType.Reactive, null, 1, true);

    [Fact]
    public async Task GenerateItineraryAsync_CompilesEverySavedMessage()
    {
        var agent = new ScriptedSynthesizer("Day 1 in Dubai.");
        var saved = new[]
        {
            Saved("Top pick: Karak House for tea."),
            Saved("The Al Fahidi walk is lovely at 16:00."),
        };

        var itinerary = await agent.GenerateItineraryAsync(TestData.Profile, saved);

        Assert.Equal("Day 1 in Dubai.", itinerary);
        Assert.Equal(2, agent.ItineraryInput?.Count);
    }

    [Fact]
    public async Task TheItineraryIsNeverAskedForWhenNothingIsSaved()
    {
        // The real agent, so the guard itself is what is under test: a model asked to plan an
        // empty day will invent one.
        var client = new ScriptedLlmClient("Day 1 in Dubai.");
        var agent = new SynthesizerAgent(client, NullLogger<SynthesizerAgent>.Instance);

        var itinerary = await agent.GenerateItineraryAsync(TestData.Profile, Array.Empty<ChatMessage>());

        Assert.Empty(client.Calls);
        Assert.Equal(string.Empty, itinerary);
    }

    [Fact]
    public async Task TheItineraryPromptAsksForOnlyWhatWasSaved()
    {
        var client = new ScriptedLlmClient("Day 1 in Dubai.");
        var agent = new SynthesizerAgent(client, NullLogger<SynthesizerAgent>.Instance);

        await agent.GenerateItineraryAsync(
            TestData.Profile,
            new[] { Saved("Top pick: Karak House for tea.") });

        var prompt = client.Calls[0].Prompt;
        Assert.Contains("traveler_profile", prompt);
        Assert.Contains("Top pick: Karak House for tea.", prompt);
        Assert.Contains("Do not add places, restaurants, or", prompt);
        Assert.Contains("Pro-Tip", prompt);
    }

    /// <summary>Captures the prompt the itinerary request was actually asked with.</summary>
    private sealed class ScriptedSynthesizer : ISynthesizerAgent
    {
        private readonly string _reply;

        public ScriptedSynthesizer(string reply) => _reply = reply;

        public List<ChatMessage>? ItineraryInput { get; private set; }

        public int ItineraryCalls { get; private set; }

        public string AgentName => "SynthesizerAgent";

        public IAsyncEnumerable<string> StreamAsync(SynthesisRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> CompleteAsync(SynthesisRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SynthesisResponse> SynthesizeAsync(SynthesisRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> ExecuteProactiveAsync(
            IReadOnlyList<ProactiveSuggestion> suggestions,
            ProactiveRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> ExecuteWelcomeAsync(UserProfile profile, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> GenerateItineraryAsync(
            UserProfile profile,
            IReadOnlyList<ChatMessage> savedMessages,
            CancellationToken cancellationToken = default)
        {
            ItineraryCalls++;
            ItineraryInput = savedMessages.ToList();
            return Task.FromResult(_reply);
        }

        public Task<string> ExecuteWeatherPivotAsync(WeatherPivot pivot, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
