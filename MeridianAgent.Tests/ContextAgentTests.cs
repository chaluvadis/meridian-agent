namespace MeridianAgent.Tests;

public class ContextAgentTests
{
    private static ContextAgent CreateAgent(params string[] responses) =>
        new(new ScriptedLlmClient(responses), NullLogger<ContextAgent>.Instance);

    [Fact]
    public async Task ExtractAsync_ParsesValidConstraints()
    {
        var client = new ScriptedLlmClient("""
            {
              "query_intent": "food",
              "relevant_dietary": ["vegetarian"],
              "avoid_list": ["crowded nightclubs"],
              "pacing_preference": "slow",
              "energy_level": "low",
              "time_sensitivity": "normal",
              "budget_range": "medium"
            }
            """);

        var agent = new ContextAgent(client, NullLogger<ContextAgent>.Instance);

        var constraints = await agent.ExtractAsync(TestData.Request());

        Assert.Equal(QueryIntent.Food, constraints.QueryIntent);
        Assert.Equal(new[] { "vegetarian" }, constraints.RelevantDietary);
        Assert.Equal(EnergyLevel.Low, constraints.EnergyLevel);
        Assert.Equal(PacingPreference.Slow, constraints.PacingPreference);
        Assert.Equal(BudgetRange.Medium, constraints.BudgetRange);
    }

    [Fact]
    public async Task ExtractAsync_UsesTheConfiguredModel()
    {
        // The configured model is what reaches the gateway, because a per-call ModelId overrides
        // whatever model the client holds: asserting the constant here would prove nothing.
        var client = new ScriptedLlmClient("""{"query_intent": "food"}""");
        var agent = new ContextAgent(
            client,
            NullLogger<ContextAgent>.Instance,
            Options.Create(new AiOptions { Model = "anthropic/claude-haiku-4.5" }));

        await agent.ExtractAsync(TestData.Request());

        Assert.Equal("anthropic/claude-haiku-4.5", client.Calls[0].Model);
        Assert.Equal("anthropic/claude-haiku-4.5", agent.Model);
    }

    [Fact]
    public async Task ExtractAsync_FallsBackToTheConfiguredDefault()
    {
        var client = new ScriptedLlmClient("""{"query_intent": "food"}""");
        var agent = new ContextAgent(client, NullLogger<ContextAgent>.Instance);

        await agent.ExtractAsync(TestData.Request());

        Assert.Equal(AiOptions.DefaultModel, client.Calls[0].Model);
    }

    [Fact]
    public async Task ExtractAsync_DefaultsWhenOnlyIntentPresent()
    {
        var agent = CreateAgent("""{"query_intent": "sightseeing"}""");

        var constraints = await agent.ExtractAsync(TestData.Request());

        Assert.Equal(QueryIntent.Sightseeing, constraints.QueryIntent);
        Assert.Null(constraints.RelevantDietary);
        Assert.Equal(EnergyLevel.Medium, constraints.EnergyLevel);
        Assert.Equal(PacingPreference.Moderate, constraints.PacingPreference);
        Assert.Null(constraints.BudgetRange);
    }

    [Fact]
    public async Task ExtractAsync_RepairsFencedJson()
    {
        var agent = CreateAgent("""
            ```json
            {"query_intent": "nightlife", "energy_level": "high"}
            ```
            """);

        var constraints = await agent.ExtractAsync(TestData.Request());

        Assert.Equal(QueryIntent.Nightlife, constraints.QueryIntent);
        Assert.Equal(EnergyLevel.High, constraints.EnergyLevel);
    }

    [Fact]
    public async Task ExtractAsync_RetriesWithValidationFeedback()
    {
        var client = new ScriptedLlmClient(
            """{"relevant_dietary": ["vegetarian"]}""",
            """{"query_intent": "food"}""");

        var agent = new ContextAgent(client, NullLogger<ContextAgent>.Instance);

        var constraints = await agent.ExtractAsync(TestData.Request());

        Assert.Equal(QueryIntent.Food, constraints.QueryIntent);
        Assert.Equal(2, client.Calls.Count);
        Assert.Contains("Your previous response was invalid", client.Calls[1].Prompt);
        Assert.Contains("query_intent", client.Calls[1].Prompt);
    }

    [Fact]
    public async Task ExtractAsync_FailsAfterExhaustingAttempts()
    {
        // Each attempt consumes: one model response (garbage) + one sanitizer response (also garbage).
        var agent = CreateAgent("nope", "still not json", "nope again", "definitely not json");

        await Assert.ThrowsAsync<Domain.Exceptions.AgentInvocationException>(() =>
            agent.ExtractAsync(TestData.Request()));
    }

    [Fact]
    public async Task ExtractAsync_IncludesProfileAndContextInPrompt()
    {
        var client = new ScriptedLlmClient("""{"query_intent": "food"}""");
        var agent = new ContextAgent(client, NullLogger<ContextAgent>.Instance);

        await agent.ExtractAsync(TestData.Request("We're exhausted - anywhere good for dinner?"));

        var prompt = client.Calls[0].Prompt;
        Assert.Contains("Dubai, UAE", prompt);
        Assert.Contains("20:45", prompt);
        Assert.Contains("vegetarian", prompt);
        Assert.Contains("exhausted", prompt);
    }
}
