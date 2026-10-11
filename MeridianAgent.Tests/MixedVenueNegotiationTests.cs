namespace MeridianAgent.Tests;

using MeridianAgent.Domain.Entities;

using MeridianAgent.Infrastructure.AI.Agents;

/// <summary>
/// A traveler who prefers "indoor" is not refused when the area offers "mixed" venues. The Search
/// Agent must weigh the sheltered part of a mixed venue against an indoor preference, propose it,
/// and name the trade — so the dead-end "everything nearby is outdoor or mixed" never reaches the
/// traveler. Asserted against the prompt text, which is where those rules live.
/// </summary>
public class MixedVenueNegotiationTests
{
    private static SearchAgent CreateAgent(params string[] responses) =>
        new(new ScriptedLlmClient(responses), NullLogger<SearchAgent>.Instance);

    [Fact]
    public async Task ProposeAsync_TellsTheModelAMixedVenueWithShelterCounts()
    {
        var client = new ScriptedLlmClient(
            """[{"venue_name": "Karak House", "venue_type": "indoor", "category": "cafe"}]""");
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance);

        await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        var prompt = client.Calls[0].Prompt;
        Assert.Contains("MIXED", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VALID MATCH", prompt);
        Assert.Contains("air-conditioned", prompt);
    }

    [Fact]
    public async Task ProposeAsync_TreatsIndoorAsASoftConstraint()
    {
        var client = new ScriptedLlmClient(
            """[{"venue_name": "Karak House", "venue_type": "indoor", "category": "cafe"}]""");
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance);

        await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        var prompt = client.Calls[0].Prompt;
        // Venue type is a preference to bend, and refusing over it is named as the failure it is.
        Assert.Contains("venue type (indoor/outdoor/mixed)", prompt);
        Assert.Contains("that is a compromise to describe, not a dead end", prompt);
        Assert.Contains("can never be broken", prompt);
    }

    [Fact]
    public async Task ProposeAsync_OnlyEnforcesIndoorWhenTheWeatherIsExtreme()
    {
        var client = new ScriptedLlmClient(
            """[{"venue_name": "Karak House", "venue_type": "indoor", "category": "cafe"}]""");
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance);

        await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        var prompt = client.Calls[0].Prompt;
        Assert.Contains("weather_temp_c > 38 OR is_raining = true", prompt);
        Assert.Contains("Below that, a mixed venue is perfectly acceptable", prompt);
        // The old 35°C blanket rule is gone: a warm evening no longer forces indoor-only.
        Assert.DoesNotContain("temperature > 35°C", prompt);
    }

    [Fact]
    public async Task ProposeAsync_ReservesTheEmptyPlanForHardConstraints()
    {
        var client = new ScriptedLlmClient(
            """[{"venue_name": "Karak House", "venue_type": "indoor", "category": "cafe"}]""");
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance);

        await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        var prompt = client.Calls[0].Prompt;
        Assert.Contains("MUST return 1 to 3 venues", prompt);
        Assert.Contains("ONLY when no venue in the entire nearby list satisfies the HARD constraints", prompt);
        Assert.Contains("Never return it because the setting, vibe, travel time, or budget was not perfect", prompt);
    }

    [Fact]
    public async Task ProposeAsync_ShowsTheModelWhichVenuesAreMixedAndWhatSheltersThem()
    {
        var client = new ScriptedLlmClient(
            """[{"venue_name": "Arabian Tea House", "venue_type": "mixed", "category": "restaurant"}]""");
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance);

        await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        var prompt = client.Calls[0].Prompt;
        Assert.Contains("\"venue_setting\":\"mixed\"", prompt);
        Assert.Contains("air-conditioned indoor seating section", prompt);
    }

    [Fact]
    public async Task ProposeAsync_TeachesOnlyATradeTheParserWouldAccept()
    {
        // The prompt example comes from the same constant the retry feedback offers, so the model
        // is never taught a phrasing the consistency rule would bounce back for a wasted retry.
        var client = new ScriptedLlmClient(
            """[{"venue_name": "Karak House", "venue_type": "indoor", "category": "cafe"}]""");
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance);

        await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        Assert.Contains(SearchAgent.MixedVenueTradeExample, client.Calls[0].Prompt);
    }

    [Fact]
    public async Task ProposeAsync_CarriesAMixedVenueCompromiseThatNamesItsShelter()
    {
        var agent = CreateAgent($$"""
            [{"venue_name": "Arabian Tea House", "venue_type": "mixed", "category": "restaurant",
              "travel_time_minutes": 11,
              "why_it_fits": "Vegetarian courtyard dining with a fully air-conditioned indoor section.",
              "compromise": "{{SearchAgent.MixedVenueTradeExample}}"}]
            """);

        var itinerary = await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        var venue = Assert.Single(itinerary.Venues);
        Assert.Equal(VenueType.Mixed, venue.VenueType);
        Assert.Contains("air-conditioned", venue.Compromise!);
    }

    [Fact]
    public async Task ProposeAsync_AcceptsTheMixedTradeOnTheFirstAttempt()
    {
        // A mixed-venue trade that names its shelter is weighable, so the agent is never asked twice.
        var client = new ScriptedLlmClient(MixedTrade(), MixedTrade());
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance);

        var itinerary = await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        Assert.Single(client.Calls);
        Assert.Contains("air-conditioned", Assert.Single(itinerary.Venues).Compromise!);
    }

    [Theory]
    [InlineData("A mixed venue, but lovely in this weather")]
    [InlineData("A mixed venue, but fine for tonight")]
    public async Task ProposeAsync_WontPassOffAMixedVenueAsAShelteredOne(string hedge)
    {
        // "Mixed" alone is not the trade: the sheltered section is what the traveler gets.
        var client = new ScriptedLlmClient(VenueWith(hedge), MixedTrade());
        var agent = new SearchAgent(client, NullLogger<SearchAgent>.Instance);

        var itinerary = await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        Assert.Equal(2, client.Calls.Count);
        Assert.Contains("naming its shelter", client.Calls[1].Prompt);
        Assert.Contains("air-conditioned", Assert.Single(itinerary.Venues).Compromise!);
    }

    [Fact]
    public async Task ProposeAsync_StillRefusesWhenTheAreaCannotFeedThem()
    {
        // The mixed rule relaxes the setting, never the hard constraints.
        var agent = CreateAgent("""
            {"error": true, "reason": "You need vegetarian, but everything nearby is a meat-focused grill house"}
            """);

        var exception = await Assert.ThrowsAsync<NoVenuesFoundException>(() =>
            agent.ProposeAsync(TestData.Constraints, TestData.Request()));

        Assert.Contains("vegetarian", exception.Reason);
    }

    private static string VenueWith(string compromise) => $$"""
        [{"venue_name": "Arabian Tea House", "venue_type": "mixed", "category": "restaurant",
          "travel_time_minutes": 11,
          "why_it_fits": "The closest vegetarian option near the old town.",
          "compromise": "{{compromise}}"}]
        """;

    private static string MixedTrade() => VenueWith(SearchAgent.MixedVenueTradeExample);
}

/// <summary>
/// The Synthesizer is the only voice the traveler hears, so both of its speaking prompts have to
/// present a mixed venue as the smart choice it is rather than an apology.
/// </summary>
public class MixedVenueVoiceTests
{
    private static readonly string[] Prompts =
    {
        typeof(SynthesizerAgent).GetField("Prompt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null) as string ?? string.Empty,
        typeof(SynthesizerAgent).GetField("QuickActionsPrompt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null) as string ?? string.Empty,
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void BothPromptsFrameAMixedVenueAsAnInsiderTip(int index)
    {
        var prompt = Prompts[index];

        Assert.Contains("MIXED", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do NOT apologize", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("insider tip", prompt, StringComparison.OrdinalIgnoreCase);
        // The sheltered part is the selling point, so the voice has to name it.
        Assert.Contains("air-conditioned indoor section", prompt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void BothPromptsNeverCallAMixedVenueADowngrade(int index)
    {
        var prompt = Prompts[index];

        Assert.Contains("downgrade", prompt);
    }

    [Fact]
    public void BothPromptsPivotConstructivelyWhenTheAreaGenuinelyConflicts()
    {
        foreach (var prompt in Prompts)
        {
            Assert.Contains("take a quick ten-minute taxi", prompt);
            Assert.Contains("fully indoor", prompt);
        }
    }
}

/// <summary>A mixed venue is only shelter when it says what its sheltered part is.</summary>
public class NearbyVenueShelterTests
{
    [Fact]
    public void AMixedVenueWithAShelterNoteOffersShelter()
    {
        var venue = new NearbyVenue
        {
            Name = "Arabian Tea House",
            IsIndoor = false,
            Setting = "mixed",
            ShelterNote = "shaded courtyard with a fully air-conditioned indoor seating section",
        };

        Assert.True(venue.IsMixed);
        Assert.True(venue.OffersShelter);
    }

    [Fact]
    public void AMixedVenueWithNothingToNameIsNotShelter()
    {
        var venue = new NearbyVenue { Name = "A courtyard", IsIndoor = false, Setting = "mixed" };

        Assert.True(venue.IsMixed);
        Assert.False(venue.OffersShelter);
    }

    [Fact]
    public void AFlatIndoorVenueOffersShelterWhateverWasStated()
    {
        var venue = new NearbyVenue { Name = "Karak House", IsIndoor = true };

        Assert.False(venue.IsMixed);
        Assert.True(venue.OffersShelter);
    }

    [Fact]
    public void TheSettingAndNoteReachTheModelInTheVenuesJson()
    {
        var venue = new NearbyVenue
        {
            Name = "Arabian Tea House",
            IsIndoor = false,
            Setting = "mixed",
            ShelterNote = "shaded courtyard with a fully air-conditioned indoor seating section",
            DietaryOptions = new[] { "vegetarian" },
        };

        var json = JsonElementReader.Serialize(new[] { venue });

        Assert.Contains("\"venue_setting\":\"mixed\"", json);
        Assert.Contains("\"shelter_note\"", json);
    }

    [Fact]
    public void AVenueThatNeverDescribedItsSettingSerializesNoExtraFields()
    {
        var json = JsonElementReader.Serialize(new[] { TestData.NearbyVenues[0] });

        Assert.DoesNotContain("venue_setting", json);
        Assert.DoesNotContain("shelter_note", json);
    }
}
