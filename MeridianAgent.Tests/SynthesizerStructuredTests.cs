namespace MeridianAgent.Tests;

using MeridianAgent.Application.Orchestrator;
using MeridianAgent.Infrastructure.AI.Agents;

public class SynthesizerStructuredResponseTests
{
    private static SynthesizerAgent CreateAgent(out ScriptedLlmClient client)
    {
        client = new ScriptedLlmClient();
        return new SynthesizerAgent(client, NullLogger<SynthesizerAgent>.Instance);
    }

    [Fact]
    public async Task SynthesizeAsync_ReturnsMessageAndQuickActions()
    {
        var agent = CreateAgent(out var client);
        client.Response = """
            {
              "message": "Top pick: Karak House.",
              "quick_actions": [
                {"label": "📍 Show on Map", "payload": "Karak House", "action_type": "navigate"},
                {"label": "✅ Accept & Plan", "payload": "Plan an evening around Karak House", "action_type": "accept_suggestion"},
                {"label": "🔄 Find Alternatives", "payload": "somewhere calmer than Karak House", "action_type": "refine_search"}
              ]
            }
            """;

        var response = await agent.SynthesizeAsync(TestData.SynthesisRequest());

        Assert.Equal("Top pick: Karak House.", response.Message);
        Assert.Equal(3, response.QuickActions.Count);

        var request = Assert.Single(client.Calls);
        Assert.True(request.JsonMode, "the structured response path asks the model for JSON");
    }

    [Fact]
    public async Task SynthesizeAsync_ParsesQuickActionsIntoTheCanonicalTypes()
    {
        var agent = CreateAgent(out var client);
        client.Response = """
            ```json
            {
              "message": "Karak House is your best fit tonight.",
              "quick_actions": [
                {"label": "📍 Show on Map", "payload": "Karak House", "action_type": "show_map"},
                {"label": "✅ Accept & Plan", "payload": "Plan an evening around Karak House", "action_type": "accept"},
                {"label": "🔄 Find Alternatives", "payload": "somewhere calmer than Karak House", "action_type": "refine_search"}
              ]
            }
            ```
            
           """;

        var response = await agent.SynthesizeAsync(TestData.SynthesisRequest());

        Assert.Equal("Karak House is your best fit tonight.", response.Message);
        Assert.Equal(3, response.QuickActions.Count);
        Assert.Equal(QuickActionType.Navigate, response.QuickActions[0].ActionType);
        Assert.Equal(QuickActionType.AcceptSuggestion, response.QuickActions[1].ActionType);
        Assert.Equal(QuickActionType.RefineSearch, response.QuickActions[2].ActionType);
        Assert.All(response.QuickActions, action => Assert.False(string.IsNullOrWhiteSpace(action.Label)));
    }

    [Fact]
    public async Task SynthesizeAsync_KeepsNewlinesFromRelaxedJson()
    {
        // SLMs frequently emit literal newlines inside JSON strings, which breaks strict parsing.
        var agent = CreateAgent(out var client);
        client.Response = """
            {
              "message": "Top pick: Karak House.\nAlternative: Al Fanar.",
              "quick_actions": []
            }
            """;

        var response = await agent.SynthesizeAsync(TestData.SynthesisRequest());

        Assert.Contains("Top pick: Karak House.", response.Message);
        Assert.Contains("Al Fanar.", response.Message);
    }

    [Fact]
    public async Task SynthesizeAsync_FallsBackToTheWholeReplyWhenTheModelAnswersInProse()
    {
        var agent = CreateAgent(out var client);
        client.Response = "Top pick: Karak House — it's quiet, it's close, and they'll do the vegetarian plate.";

        var response = await agent.SynthesizeAsync(TestData.SynthesisRequest());

        Assert.Equal(client.Response.Trim(), response.Message);
        Assert.Empty(response.QuickActions);
    }

    [Fact]
    public async Task SynthesizeAsync_RecoversTheMessageFromABrokenEnvelope()
    {
        var agent = CreateAgent(out var client);
        client.Response = """
            Sure! {
              "message": "Karak House is six minutes away.",
              "quick_actions": [{"label": "📌 Show on Map", "payload": "Karak House", "action_type": "show_map"},
            }
            """;

        var response = await agent.SynthesizeAsync(TestData.SynthesisRequest());

        // The envelope is hopeless, but the readable message must still reach the traveler.
        Assert.Contains("Karak House is six minutes away.", response.Message);
        Assert.DoesNotContain("{\"message\"", response.Message);
    }

    [Fact]
    public async Task SynthesizeAsync_DropsChipsWithoutLabelsAndCapsAtThree()
    {
        var agent = CreateAgent(out var client);
        client.Response = """
            {
              "message": "Two good options.",
              "quick_actions": [
                {"label": "📌 Show on Map", "payload": "Karak House", "action_type": "navigate"},
                {"payload": "no label here", "action_type": "accept"},
                {"label": "🔄 Find Alternatives", "payload": "calmer", "action_type": "refine"},
                {"label": "🧭 One too many", "payload": "extra", "action_type": "accept"}
              ]
            }
            """;

        var response = await agent.SynthesizeAsync(TestData.SynthesisRequest());

        Assert.Equal(3, response.QuickActions.Count);
        Assert.DoesNotContain(response.QuickActions, action => action.Label == "no label here");
        Assert.All(response.QuickActions.Take(2), action => Assert.NotNull(action.Label));
    }

    [Fact]
    public async Task SynthesizeAsync_UsesTheLabelAsPayloadWhenPayloadIsMissing()
    {
        var agent = CreateAgent(out var client);
        client.Response = """
            {
              "message": "Karak House it is.",
              "quick_actions": [
                {"label": "📌 Show on Map", "action_type": "show_map"}
              ]
            }
            """;

        var response = await agent.SynthesizeAsync(TestData.SynthesisRequest());

        var action = Assert.Single(response.QuickActions);
        Assert.Equal("📌 Show on Map", action.Payload);
    }

    [Fact]
    public async Task SynthesizeAsync_MapsUnknownActionTypesToRefineSearch()
    {
        var agent = CreateAgent(out var client);
        client.Response = """
            {
              "message": "Karak House it is.",
              "quick_actions": [
                {"label": "🤷 Something else", "payload": "x", "action_type": "teleport"}
              ]
            }
            """;

        var response = await agent.SynthesizeAsync(TestData.SynthesisRequest());

        var action = Assert.Single(response.QuickActions);
        Assert.Equal(QuickActionType.RefineSearch, action.ActionType);
    }

    [Fact]
    public async Task SynthesizeAsync_AnswersWithAGracefulMessageWhenTheModelReturnsNothing()
    {
        var agent = CreateAgent(out var client);
        client.Response = "   ";

        var response = await agent.SynthesizeAsync(TestData.SynthesisRequest());

        Assert.Contains("couldn't put an answer together", response.Message);
        Assert.Empty(response.QuickActions);
    }

    [Fact]
    public void QuickActionTypeExtensions_MapsModelStrings()
    {
        Assert.Equal(QuickActionType.Navigate, QuickActionTypeExtensions.FromModelString("show_map"));
        Assert.Equal(QuickActionType.Navigate, QuickActionTypeExtensions.FromModelString("navigate"));
        Assert.Equal(QuickActionType.Navigate, QuickActionTypeExtensions.FromModelString("Open on Map"));
        Assert.Equal(QuickActionType.AcceptSuggestion, QuickActionTypeExtensions.FromModelString("accept_suggestion"));
        Assert.Equal(QuickActionType.AcceptSuggestion, QuickActionTypeExtensions.FromModelString(" accept "));
        Assert.Equal(QuickActionType.AcceptSuggestion, QuickActionTypeExtensions.FromModelString("plan_this"));
        Assert.Equal(QuickActionType.RefineSearch, QuickActionTypeExtensions.FromModelString("refine_search"));
        Assert.Equal(QuickActionType.RefineSearch, QuickActionTypeExtensions.FromModelString("find_alternatives"));
        Assert.Equal(QuickActionType.RefineSearch, QuickActionTypeExtensions.FromModelString("nonsense"));
        Assert.Equal(QuickActionType.RefineSearch, QuickActionTypeExtensions.FromModelString(null));
    }

    [Fact]
    public void QuickActionTypeExtensions_RoundTripsThroughModelStrings()
    {
        Assert.Equal(
            QuickActionType.Navigate,
            QuickActionTypeExtensions.FromModelString(QuickActionType.Navigate.ToModelString()));
        Assert.Equal(
            QuickActionType.AcceptSuggestion,
            QuickActionTypeExtensions.FromModelString(QuickActionType.AcceptSuggestion.ToModelString()));
        Assert.Equal(
            QuickActionType.RefineSearch,
            QuickActionTypeExtensions.FromModelString(QuickActionType.RefineSearch.ToModelString()));
    }
}

public class TravelQueryOrchestratorStructuredTests
{
    private static TravelQueryOrchestrator CreateOrchestrator(FakeSynthesizerAgent synthesizer) => new(
        new FakeContextAgent(),
        new FakeSearchAgent(),
        new FakeSafeguardAgent(),
        synthesizer,
        NullLogger<TravelQueryOrchestrator>.Instance,
        blackboard: new AgentBlackboard());

    [Fact]
    public async Task SynthesizeAsync_ReturnsMessageQuickActionsAndItinerary()
    {
        var synthesizer = new FakeSynthesizerAgent();
        var orchestrator = CreateOrchestrator(synthesizer);

        var response = await orchestrator.SynthesizeAsync(TestData.Request());

        Assert.Equal("Top pick: Karak House.", response.ResponseText);
        Assert.Single(response.Itinerary.Venues);
        Assert.False(response.SafeguardReport.IsFlagged);
        Assert.Equal(3, response.QuickActions.Count);
        Assert.Single(synthesizer.Requests);

        // The structured path publishes to the same blackboard as the streaming one.
        Assert.Equal("Top pick: Karak House.", orchestrator.Blackboard.Response);
        Assert.NotNull(orchestrator.Blackboard.Itinerary);
    }

    [Fact]
    public async Task SynthesizeAsync_PassesTheSafeguardReportToTheSynthesizer()
    {
        var synthesizer = new FakeSynthesizerAgent();
        var orchestrator = new TravelQueryOrchestrator(
            new FakeContextAgent(),
            new FakeSearchAgent(),
            new FakeSafeguardAgent { Result = TestData.FlaggedReport },
            synthesizer,
            NullLogger<TravelQueryOrchestrator>.Instance,
            blackboard: new AgentBlackboard());

        var response = await orchestrator.SynthesizeAsync(TestData.Request());

        Assert.True(response.SafeguardReport.IsFlagged);

        var synthesis = Assert.Single(synthesizer.Requests);
        Assert.True(synthesis.Report.IsFlagged);
    }

    [Fact]
    public async Task SynthesizeAsync_ReturnsFriendlyTextWhenNoVenuesFit()
    {
        var orchestrator = new TravelQueryOrchestrator(
            new FakeContextAgent(),
            new FakeSearchAgent { NoVenuesFailures = 2, NoVenuesReason = "everything is closed" },
            new FakeSafeguardAgent(),
            new FakeSynthesizerAgent(),
            NullLogger<TravelQueryOrchestrator>.Instance,
            blackboard: new AgentBlackboard());

        var response = await orchestrator.SynthesizeAsync(TestData.Request());

        Assert.Contains("everything is closed", response.ResponseText);
        Assert.Contains("indoor", response.ResponseText);
        Assert.Empty(response.QuickActions);
        Assert.Empty(response.Itinerary.Venues);
    }

    [Fact]
    public async Task SynthesizeAsync_AnswersEvenWhenTheSynthesizerThrows()
    {
        var orchestrator = new TravelQueryOrchestrator(
            new FakeContextAgent(),
            new FakeSearchAgent(),
            new FakeSafeguardAgent(),
            new FakeSynthesizerAgent { SynthesisError = new InvalidOperationException("model down") },
            NullLogger<TravelQueryOrchestrator>.Instance,
            blackboard: new AgentBlackboard());

        var response = await orchestrator.SynthesizeAsync(TestData.Request());

        // The pipeline still answers; the failure is contained in one stage.
        Assert.Contains("couldn't put an answer together", response.ResponseText);
        Assert.Empty(response.QuickActions);
        Assert.Single(response.Itinerary.Venues);
    }
}
