namespace MeridianAgent.Tests;

public class SearchAgentTests
{
    private static SearchAgent CreateAgent(params string[] responses) =>
        new(new ScriptedLlmClient(responses), NullLogger<SearchAgent>.Instance);

    [Fact]
    public async Task ProposeAsync_BuildsItineraryFromArray()
    {
        var agent = CreateAgent("""
            [
              {
                "venue_name": "Karak House",
                "venue_type": "indoor",
                "category": "cafe",
                "travel_time_minutes": 6,
                "why_it_fits": "Quiet and vegetarian.",
                "estimated_duration": "45 minutes",
                "best_time_to_visit": "Tonight"
              }
            ]
            """);

        var itinerary = await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        var venue = Assert.Single(itinerary.Venues);
        Assert.Equal("Karak House", venue.Name);
        Assert.Equal(VenueType.Indoor, venue.VenueType);
        Assert.Equal(VenueCategory.Cafe, venue.Category);
        Assert.Equal(6, venue.TravelTimeMinutes);
    }

    [Fact]
    public async Task ProposeAsync_PromptCarriesReality()
    {
        var client = new ScriptedLlmClient(
            """[{"venue_name": "Karak House", "venue_type": "indoor", "category": "cafe"}]""");
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance);

        await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        var prompt = client.Calls[0].Prompt;
        Assert.Contains("\"is_raining\": false", prompt);
        Assert.Contains("31", prompt);
        Assert.Contains("20:45", prompt);
        Assert.Contains("Karak House", prompt);
        Assert.Equal(AiOptions.DefaultModel, client.Calls[0].Model);
    }

    [Fact]
    public async Task ProposeAsync_AsksForTheModelTheConfigurationNames()
    {
        // AI__Model is the documented one-line switch: the model on the wire is what answers.
        var client = new ScriptedLlmClient(
            """[{"venue_name": "Karak House", "venue_type": "indoor", "category": "cafe"}]""");
        var agent = new SearchAgent(
            client,
            NullLogger<SearchAgent>.Instance,
            Options.Create(new AiOptions { Model = "google/gemini-2.5-flash" }));

        await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        Assert.Equal("google/gemini-2.5-flash", client.Calls[0].Model);
    }

    [Fact]
    public async Task ProposeAsync_ThrowsNoVenuesFoundOnErrorObject()
    {
        var agent = CreateAgent("""{"error": true, "reason": "everything nearby is closed after 22:00"}""");

        var exception = await Assert.ThrowsAsync<NoVenuesFoundException>(() =>
            agent.ProposeAsync(TestData.Constraints, TestData.Request()));

        Assert.Contains("closed after 22:00", exception.Reason);
    }

    [Fact]
    public async Task ProposeAsync_ThrowsNoVenuesFoundOnEmptyArray()
    {
        var agent = CreateAgent("[]");

        await Assert.ThrowsAsync<NoVenuesFoundException>(() =>
            agent.ProposeAsync(TestData.Constraints, TestData.Request()));
    }

    [Fact]
    public async Task ProposeAsync_RejectsMoreThanThreeVenues()
    {
        const string fourVenues = """
            [
              {"venue_name": "A", "venue_type": "indoor", "category": "cafe"},
              {"venue_name": "B", "venue_type": "indoor", "category": "cafe"},
              {"venue_name": "C", "venue_type": "indoor", "category": "cafe"},
              {"venue_name": "D", "venue_type": "indoor", "category": "cafe"}
            ]
            """;
        var agent = CreateAgent(fourVenues, fourVenues);

        await Assert.ThrowsAsync<AgentInvocationException>(() =>
            agent.ProposeAsync(TestData.Constraints, TestData.Request()));
    }

    [Fact]
    public async Task ProposeAsync_RequiresVenueNameTypeAndCategory()
    {
        const string missingName = """
            [
              {"venue_type": "indoor", "category": "cafe"}
            ]
            """;
        var agent = CreateAgent(missingName, missingName);

        await Assert.ThrowsAsync<AgentInvocationException>(() =>
            agent.ProposeAsync(TestData.Constraints, TestData.Request()));
    }

    [Fact]
    public async Task ProposeAsync_UsesOverriddenModel()
    {
        var client = new ScriptedLlmClient(
            """[{"venue_name": "A", "venue_type": "indoor", "category": "cafe"}]""");
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance, model: "custom-model");

        await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        Assert.Equal("custom-model", client.Calls[0].Model);
    }
}
