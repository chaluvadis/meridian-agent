namespace MeridianAgent.Tests;

public class ProactiveSuggestionAgentTests
{
    private static ProactiveSuggestionAgent CreateAgent(params string[] responses) => new(
        new ScriptedLlmClient(responses),
        NullLogger<ProactiveSuggestionAgent>.Instance);

    private static ProactiveRequest Request() => new(
        TestData.Profile,
        TestData.Context,
        TestData.NearbyVenues,
        TestData.Rulebook);

    [Fact]
    public async Task GenerateAsync_RanksSuggestionsByConfidenceDescending()
    {
        var agent = CreateAgent("""
            {
              "suggestions": [
                { "title": "an early souk stroll", "rationale": "quiet right after sunset", "confidence_score": 0.61, "suggested_timing": "tonight", "venue_type": "activity", "interest": "old town souks" },
                { "title": "a quiet rooftop before it gets busy", "rationale": "six minutes away", "confidence_score": 0.82, "suggested_timing": "in the next hour", "venue_type": "cafe", "interest": "rooftop cafés" }
              ]
            }
            """);

        var suggestions = await agent.GenerateAsync(Request());

        Assert.Equal(0.82, suggestions[0].ConfidenceScore);
        Assert.Equal("a quiet rooftop before it gets busy", suggestions[0].Title);
        Assert.Equal("six minutes away", suggestions[0].Rationale);
        Assert.Equal("in the next hour", suggestions[0].SuggestedTiming);
        Assert.Equal("cafe", suggestions[0].VenueType);
        Assert.Equal("rooftop cafés", suggestions[0].Interest);
        Assert.Equal(0.61, suggestions[1].ConfidenceScore);
    }

    [Fact]
    public async Task GenerateAsync_AcceptsAnEmptySuggestionsList()
    {
        var agent = CreateAgent("""{"suggestions": []}""");

        var suggestions = await agent.GenerateAsync(Request());

        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task GenerateAsync_IgnoresProseAndFencesAroundJson()
    {
        var agent = CreateAgent("""
            Sure, here you go:
            ```json
            {"suggestions": [{"title": "a calm coffee stop", "rationale": "you look tired", "confidence_score": 0.9}]}
            ```
            Hope that helps!
            """);

        var suggestions = await agent.GenerateAsync(Request());

        var suggestion = Assert.Single(suggestions);
        Assert.Equal("a calm coffee stop", suggestion.Title);
        Assert.Equal(0.9, suggestion.ConfidenceScore);
    }

    [Fact]
    public async Task GenerateAsync_ThrowsWhenTheModelReportsAnError()
    {
        var agent = CreateAgent(
            """{"error": true, "reason": "nothing around them fits"}""",
            """{"error": true, "reason": "nothing around them fits"}""");

        await Assert.ThrowsAsync<AgentInvocationException>(() => agent.GenerateAsync(Request()));
    }

    [Fact]
    public async Task GenerateAsync_RejectsOutOfRangeConfidenceScores()
    {
        var agent = CreateAgent(
            """{"suggestions": [{"title": "impossible odds", "rationale": "too sure", "confidence_score": 1.5}]}""",
            """{"suggestions": [{"title": "impossible odds", "rationale": "too sure", "confidence_score": 1.5}]}""");

        await Assert.ThrowsAsync<AgentInvocationException>(() => agent.GenerateAsync(Request()));
    }
}
