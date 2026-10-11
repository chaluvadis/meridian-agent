namespace MeridianAgent.Tests;

public class TravelQueryOrchestratorTests
{
    private static TravelQueryOrchestrator CreateOrchestrator(
        FakeContextAgent? contextAgent = null,
        FakeSearchAgent? searchAgent = null,
        FakeSafeguardAgent? safeguardAgent = null,
        FakeSynthesizerAgent? synthesizerAgent = null)
    {
        return new TravelQueryOrchestrator(
            contextAgent ?? new FakeContextAgent(),
            searchAgent ?? new FakeSearchAgent(),
            safeguardAgent ?? new FakeSafeguardAgent(),
            synthesizerAgent ?? new FakeSynthesizerAgent(),
            NullLogger<TravelQueryOrchestrator>.Instance,
            blackboard: new AgentBlackboard());
    }

    [Fact]
    public async Task StreamAsync_RunsAllFourAgentsInOrder()
    {
        var context = new FakeContextAgent();
        var search = new FakeSearchAgent();
        var safeguard = new FakeSafeguardAgent { Result = TestData.FlaggedReport };
        var synthesizer = new FakeSynthesizerAgent();
        var orchestrator = CreateOrchestrator(context, search, safeguard, synthesizer);

        var tokens = new List<string>();
        await foreach (var token in orchestrator.StreamAsync(TestData.Request()))
        {
            tokens.Add(token);
        }

        Assert.Equal(new[] { "Top ", "pick: ", "Karak House." }, tokens);
        Assert.Single(context.Requests);
        Assert.Single(search.Requests);
        Assert.Single(synthesizer.Requests);

        var synthesis = synthesizer.Requests[0];
        Assert.Equal(TestData.Request().Query, synthesis.Query);
        Assert.True(synthesis.Report.IsFlagged);
        Assert.Single(synthesis.Itinerary.Venues);
    }

    [Fact]
    public async Task StreamAsync_PublishesEveryStageToBlackboard()
    {
        var orchestrator = CreateOrchestrator();

        await DrainAsync(orchestrator.StreamAsync(TestData.Request()));

        Assert.Equal(TestData.Request().Query, orchestrator.Blackboard.Query);
        Assert.Equal(QueryIntent.Food, orchestrator.Blackboard.Constraints!.QueryIntent);
        Assert.Single(orchestrator.Blackboard.Itinerary!.Venues);
        Assert.Equal(SafeguardStatus.Cleared, orchestrator.Blackboard.SafeguardReport!.Status);
        Assert.NotNull(orchestrator.Blackboard.Response);

        var historyKeys = orchestrator.Blackboard.History.Select(h => h.Key);
        Assert.Contains(AgentBlackboard.Keys.Constraints, historyKeys);
        Assert.Contains(AgentBlackboard.Keys.Itinerary, historyKeys);
        Assert.Contains(AgentBlackboard.Keys.SafeguardReport, historyKeys);
        Assert.Contains(AgentBlackboard.Keys.Response, historyKeys);
    }

    [Fact]
    public async Task StreamAsync_YieldsFriendlyFallbackWhenNoVenues()
    {
        var search = new FakeSearchAgent { NoVenuesFailures = 2, NoVenuesReason = "everything is closed" };
        var synthesizer = new FakeSynthesizerAgent();
        var orchestrator = CreateOrchestrator(searchAgent: search, synthesizerAgent: synthesizer);

        var response = await DrainAsync(orchestrator.StreamAsync(TestData.Request()));

        Assert.Contains("everything is closed", response);
        Assert.Contains("indoor", response);
        Assert.Empty(synthesizer.Requests);
        Assert.Empty(orchestrator.Blackboard.Itinerary!.Venues);
    }

    [Fact]
    public async Task StreamAsync_YieldsGracefulMessageWhenSearchAgentFails()
    {
        var search = new FakeSearchAgent { Error = new InvalidOperationException("ollama down") };
        var synthesizer = new FakeSynthesizerAgent();
        var orchestrator = CreateOrchestrator(searchAgent: search, synthesizerAgent: synthesizer);

        var response = await DrainAsync(orchestrator.StreamAsync(TestData.Request()));

        Assert.Contains("couldn't build a plan", response);
        Assert.Empty(synthesizer.Requests);
    }

    [Fact]
    public async Task StreamAsync_YieldsGracefulMessageWhenContextAgentFails()
    {
        var context = new FakeContextAgent { Error = new InvalidOperationException("model missing") };
        var synthesizer = new FakeSynthesizerAgent();
        var orchestrator = CreateOrchestrator(contextAgent: context, synthesizerAgent: synthesizer);

        var response = await DrainAsync(orchestrator.StreamAsync(TestData.Request()));

        // The traveler is told what they can do (ask again, visit settings) rather than being
        // handed a technical apology with no next step.
        Assert.Contains("technical hiccup reading your profile", response);
        Assert.Contains("settings", response);
        Assert.Empty(synthesizer.Requests);
    }

    [Fact]
    public async Task PrepareAsync_NormalizesTheProfileBeforeAnyAgentRuns()
    {
        // A null list is the one profile defect that used to surface as the generic fallback; the
        // pipeline entry point now coalesces it so the agents and the serializers never see null.
        var context = new FakeContextAgent();
        var request = TestData.Request() with
        {
            Profile = UserProfile.Default with { Interests = null!, AvoidList = null! },
        };

        await DrainAsync(CreateOrchestrator(contextAgent: context).StreamAsync(request));

        var profile = context.Requests[0].Profile;
        Assert.NotNull(profile.Interests);
        Assert.NotNull(profile.AvoidList);
        Assert.Empty(profile.Interests);
        Assert.Empty(profile.AvoidList);
    }

    [Fact]
    public async Task StreamAsync_AnswersEvenWhenSafeguardAgentFails()
    {
        var safeguard = new FakeSafeguardAgent { Error = new InvalidOperationException("rulebook unreadable") };
        var synthesizer = new FakeSynthesizerAgent();
        var orchestrator = CreateOrchestrator(safeguardAgent: safeguard, synthesizerAgent: synthesizer);

        var response = await DrainAsync(orchestrator.StreamAsync(TestData.Request()));

        Assert.Contains("Top pick", response);
        Assert.Single(synthesizer.Requests);
        Assert.False(synthesizer.Requests[0].Report.IsFlagged);
        Assert.Equal(MeridianAgent.Domain.SafeguardStatus.Cleared, orchestrator.Blackboard.SafeguardReport!.Status);
    }

    [Fact]
    public async Task StreamAsync_NarrowsRulebookWhenNarrowerAvailable()
    {
        var narrower = new RecordingNarrower();
        var orchestrator = new TravelQueryOrchestrator(
            new FakeContextAgent(),
            new FakeSearchAgent(),
            new FakeSafeguardAgent(),
            new FakeSynthesizerAgent(),
            NullLogger<TravelQueryOrchestrator>.Instance,
            narrower,
            new AgentBlackboard());

        await DrainAsync(orchestrator.StreamAsync(TestData.Request()));

        Assert.Single(narrower.Calls);
    }

    [Fact]
    public async Task CompleteAsync_AggregatesStreamedResponse()
    {
        var orchestrator = CreateOrchestrator();

        var response = await orchestrator.CompleteAsync(TestData.Request());

        Assert.Equal("Top pick: Karak House.", response.ResponseText);
        Assert.Single(response.Itinerary.Venues);
        Assert.False(response.SafeguardReport.IsFlagged);
    }

    [Fact]
    public async Task CompleteAsync_ReturnsFallbackTextWhenNothingFits()
    {
        var search = new FakeSearchAgent { NoVenuesFailures = 2, NoVenuesReason = "all closed" };
        var orchestrator = CreateOrchestrator(searchAgent: search);

        var response = await orchestrator.CompleteAsync(TestData.Request());

        Assert.Contains("all closed", response.ResponseText);
        Assert.Empty(response.Itinerary.Venues);
    }

    [Fact]
    public async Task StreamAsync_RelaxesSoftConstraintsWhenStrictSearchFindsNothing()
    {
        var search = new FakeSearchAgent { NoVenuesFailures = 1, NoVenuesReason = "interests could not be matched" };
        var synthesizer = new FakeSynthesizerAgent();
        var orchestrator = CreateOrchestrator(searchAgent: search, synthesizerAgent: synthesizer);

        var tokens = new List<string>();
        await foreach (var token in orchestrator.StreamAsync(TestData.Request()))
        {
            tokens.Add(token);
        }

        // The strict attempt ran first, then one relaxed retry that succeeded.
        Assert.Equal(2, search.Requests.Count);
        Assert.Equal(new[] { "Top ", "pick: ", "Karak House." }, tokens);
        Assert.Single(orchestrator.Blackboard.Itinerary!.Venues);

        // The relaxed call keeps the hard constraints and drops the soft ones.
        var relaxed = search.ConstraintsSeen[1];
        Assert.Null(relaxed.RelevantInterests);
        Assert.Null(relaxed.BudgetRange);
        Assert.Equal(EnergyLevel.Medium, relaxed.EnergyLevel);
        Assert.Equal(TimeSensitivity.Flexible, relaxed.TimeSensitivity);
        Assert.NotNull(relaxed.RelevantDietary);
        Assert.Contains("vegetarian", relaxed.RelevantDietary!);
        Assert.NotNull(relaxed.AvoidList);
        Assert.Contains("crowded nightclubs", relaxed.AvoidList!);
        Assert.Equal(QueryIntent.Food, relaxed.QueryIntent);
    }

    [Fact]
    public async Task StreamAsync_FallsBackWhenRelaxedSearchAlsoFindsNothing()
    {
        var search = new FakeSearchAgent { NoVenuesFailures = 2, NoVenuesReason = "even relaxed constraints found nothing" };
        var synthesizer = new FakeSynthesizerAgent();
        var orchestrator = CreateOrchestrator(searchAgent: search, synthesizerAgent: synthesizer);

        var response = await DrainAsync(orchestrator.StreamAsync(TestData.Request()));

        Assert.Equal(2, search.Requests.Count);
        Assert.Contains("even relaxed constraints found nothing", response);
        Assert.Contains("indoor", response);
        Assert.Empty(synthesizer.Requests);
    }

    [Fact]
    public async Task StreamAsync_RethrowsCancellationFromSafeguardStage()
    {
        var safeguard = new FakeSafeguardAgent { Error = new OperationCanceledException() };
        var orchestrator = CreateOrchestrator(safeguardAgent: safeguard);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            DrainAsync(orchestrator.StreamAsync(TestData.Request())));
    }

    private static async Task<string> DrainAsync(IAsyncEnumerable<string> stream)
    {
        var builder = new StringBuilder();
        await foreach (var token in stream)
        {
            builder.Append(token);
        }

        return builder.ToString();
    }

    private sealed class RecordingNarrower : MeridianAgent.Application.Abstractions.IRulebookNarrower
    {
        public List<string> Calls { get; } = new();

        public Task<MeridianAgent.Domain.Entities.DestinationRulebook> NarrowAsync(
            MeridianAgent.Domain.Entities.DestinationRulebook rulebook,
            string query,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(query);
            return Task.FromResult(rulebook);
        }
    }
}
