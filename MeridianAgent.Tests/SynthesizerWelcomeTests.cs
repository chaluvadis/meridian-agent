namespace MeridianAgent.Tests;

public class SynthesizerWelcomeTests
{
    private static SynthesizerAgent CreateAgent(out ScriptedLlmClient client)
    {
        client = new ScriptedLlmClient("Welcome, Nadia!");
        return new SynthesizerAgent(client, NullLogger<SynthesizerAgent>.Instance);
    }

    [Fact]
    public async Task ExecuteWelcomeAsync_StreamsTheModelGreeting()
    {
        var agent = CreateAgent(out var client);

        var response = await agent.ExecuteWelcomeAsync(TestData.Profile);

        Assert.Equal("Welcome, Nadia!", response.Trim());
        Assert.Single(client.Calls);
    }

    [Fact]
    public async Task ExecuteWelcomeAsync_PromptCarriesTheTravelersPreferences()
    {
        var agent = CreateAgent(out var client);

        await agent.ExecuteWelcomeAsync(TestData.Profile);

        var request = Assert.Single(client.Calls);
        Assert.False(request.JsonMode);
        Assert.True(request.Temperature is > 0.5 and < 0.9);

        // The agent must be able to prove it listened: name, style, interests, dietary rules.
        Assert.Contains("Aria", request.Prompt);
        Assert.Contains("old town souks", request.Prompt);
        Assert.Contains("vegetarian", request.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("crowded mega malls", request.Prompt);
        Assert.Contains("Dubai, UAE", request.Prompt);
    }

    [Fact]
    public async Task ExecuteWelcomeAsync_DescribesOnboardingPreferencesWhenPresent()
    {
        var agent = CreateAgent(out var client);

        var draft = new TravelerOnboardingDraft
        {
            DisplayName = "Nadia",
            TravelStyle = "Cultural Immersion",
            BudgetComfort = "Money No Object",
            TravelGroup = "Friends Group",
            SpiceTolerance = 5,
        };
        TravelerOnboardingDraft.Toggle(draft.CoreInterests, "Architecture");
        TravelerOnboardingDraft.Toggle(draft.HardAvoids, "Night Markets");

        var profile = draft.BuildProfile("default", "Dubai, UAE", null, null, onboardingCompleted: true, pendingWelcome: true);

        await agent.ExecuteWelcomeAsync(profile);

        var request = Assert.Single(client.Calls);
        Assert.Contains("Cultural Immersion", request.Prompt);
        Assert.Contains("Architecture", request.Prompt);
        Assert.Contains("Night Markets", request.Prompt);
        Assert.Contains("Friends Group", request.Prompt);
        Assert.Contains("Fiery", request.Prompt);
    }

    [Fact]
    public async Task ExecuteWelcomeAsync_FallsBackToDestinationWhenNoneIsSet()
    {
        var agent = CreateAgent(out var client);

        await agent.ExecuteWelcomeAsync(UserProfile.Default);

        var request = Assert.Single(client.Calls);
        Assert.Contains("their destination", request.Prompt);
        Assert.Contains("none shared", request.Prompt);
    }
}
