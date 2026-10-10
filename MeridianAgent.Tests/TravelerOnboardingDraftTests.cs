namespace MeridianAgent.Tests;

public class TravelerOnboardingDraftTests
{
    [Fact]
    public void BuildWelcomeFallback_MentionsTheTravelersNameAndTastes()
    {
        var draft = new TravelerOnboardingDraft
        {
            DisplayName = "Nadia",
            TravelStyle = "Cultural Immersion",
        };
        TravelerOnboardingDraft.Toggle(draft.CoreInterests, "Architecture");
        TravelerOnboardingDraft.Toggle(draft.CoreInterests, "History");
        TravelerOnboardingDraft.Toggle(draft.HardAvoids, "Crowded Places");

        var welcome = draft.BuildProfile(
            "default",
            "Dubai, UAE",
            null,
            null,
            onboardingCompleted: true,
            pendingWelcome: true)
            .BuildWelcomeFallback();

        Assert.Contains("Nadia", welcome);
        Assert.Contains("cultural immersion", welcome);
        Assert.Contains("Architecture", welcome);
        Assert.Contains("Crowded Places", welcome);
        Assert.EndsWith("I'll keep it in mind.", welcome);
    }

    [Fact]
    public void BuildWelcomeFallback_UsesLegacyInterestsWhenOnboardingFieldsAreEmpty()
    {
        // TestData.Profile predates onboarding: interests live in the agent-facing field only.
        var welcome = TestData.Profile.BuildWelcomeFallback();

        Assert.Contains("Aria", welcome);
        Assert.Contains("old town souks", welcome);
        Assert.Contains("crowded mega malls", welcome);
    }

    [Fact]
    public void Defaults_AreSensibleWhenTheTravelerSkipsAQuestion()
    {
        var draft = new TravelerOnboardingDraft { DisplayName = "Nadia" };

        Assert.Equal("Balanced", draft.TravelStyle);
        Assert.Equal("Varies", draft.EnergyPattern);
        Assert.Equal("Mid-Range", draft.BudgetComfort);
        Assert.Equal("Solo", draft.TravelGroup);
        Assert.True(draft.FirstTimeVisitor);
        Assert.Equal(2, draft.SpiceTolerance);
        Assert.True(draft.IsNameProvided);
    }

    [Fact]
    public void Toggle_AddsThenRemovesValuesFromTheMultiSelects()
    {
        var draft = new TravelerOnboardingDraft();

        TravelerOnboardingDraft.Toggle(draft.CoreInterests, "Photography");
        TravelerOnboardingDraft.Toggle(draft.DietaryRestrictions, "Vegan");

        Assert.Contains("Photography", draft.CoreInterests);
        Assert.Contains("Vegan", draft.DietaryRestrictions);

        TravelerOnboardingDraft.Toggle(draft.CoreInterests, "Photography");

        Assert.DoesNotContain("Photography", draft.CoreInterests);
        Assert.Contains("Vegan", draft.DietaryRestrictions);
    }

    [Fact]
    public void BuildProfile_MapsOnboardingAnswersOntoTheAgentFacingFields()
    {
        var draft = new TravelerOnboardingDraft
        {
            DisplayName = "  Nadia  ",
            TravelStyle = "Slow Explorer",
            BudgetComfort = "Money No Object",
            TravelGroup = "Family with Kids",
            FirstTimeVisitor = false,
            SpiceTolerance = 4,
        };
        TravelerOnboardingDraft.Toggle(draft.DietaryRestrictions, "Halal");
        TravelerOnboardingDraft.Toggle(draft.CoreInterests, "Architecture");
        TravelerOnboardingDraft.Toggle(draft.CoreInterests, "History");
        TravelerOnboardingDraft.Toggle(draft.HardAvoids, "Crowded Places");

        var profile = draft.BuildProfile(
            "default",
            "Dubai, UAE",
            "Downtown Dubai",
            "light scarf in bag",
            onboardingCompleted: true,
            pendingWelcome: true);

        // Agent-facing contract.
        Assert.Equal("Nadia", profile.DisplayName);
        Assert.Equal(PacingPreference.Slow, profile.Pacing);
        Assert.Equal(BudgetRange.High, profile.Budget);
        Assert.Equal(GroupComposition.Family, profile.GroupComposition);
        Assert.Equal(TravelerProfile.Experienced, profile.TravelerProfile);
        Assert.Equal(new[] { "Halal" }, profile.DietaryRules);
        Assert.Contains("Architecture", profile.Interests);
        Assert.Contains("Crowded Places", profile.AvoidList);

        // Captured preferences are stored verbatim.
        Assert.Equal(4, profile.SpiceTolerance);
        Assert.Equal("Slow Explorer", profile.TravelStyle);
        Assert.Contains("Halal", profile.DietaryRestrictions);
        Assert.Contains("History", profile.CoreInterests);
        Assert.Contains("Crowded Places", profile.HardAvoids);

        // Destination fields the wizard does not ask for survive the round trip.
        Assert.Equal("Dubai, UAE", profile.Destination);
        Assert.Equal("Downtown Dubai", profile.HomeBase);
        Assert.Equal("light scarf in bag", profile.AttireNote);

        Assert.True(profile.OnboardingCompleted);
        Assert.True(profile.PendingWelcome);
    }

    [Fact]
    public void BuildProfile_DropsTheNoneChoiceFromTheAgentsDietaryRules()
    {
        var draft = new TravelerOnboardingDraft { DisplayName = "Sam" };
        TravelerOnboardingDraft.Toggle(draft.DietaryRestrictions, "None");
        TravelerOnboardingDraft.Toggle(draft.DietaryRestrictions, "Vegetarian");

        var profile = draft.BuildProfile("default", "Dubai, UAE", null, null, onboardingCompleted: true, pendingWelcome: true);

        Assert.Equal(new[] { "Vegetarian" }, profile.DietaryRules);
        Assert.Contains("None", profile.DietaryRestrictions);
    }

    [Fact]
    public void From_RestoresADraftFromASavedProfile()
    {
        var draft = new TravelerOnboardingDraft
        {
            DisplayName = "Nadia",
            TravelStyle = "Efficient",
            EnergyPattern = "Night Owl",
            BudgetComfort = "Splurge Occasionally",
            Mobility = "Minimize Walking",
            TravelGroup = "Couple",
            SpiceTolerance = 3,
            FirstTimeVisitor = false,
        };
        TravelerOnboardingDraft.Toggle(draft.DietaryRestrictions, "Vegan");
        TravelerOnboardingDraft.Toggle(draft.CoreInterests, "Art");
        TravelerOnboardingDraft.Toggle(draft.HardAvoids, "Tourist Traps");
        TravelerOnboardingDraft.Toggle(draft.FavoriteCuisines, "Thai");
        TravelerOnboardingDraft.Toggle(draft.DiningVibe, "Street Food");

        var profile = draft.BuildProfile("default", "Dubai, UAE", null, null, onboardingCompleted: true, pendingWelcome: false);
        var restored = TravelerOnboardingDraft.From(profile);

        Assert.Equal("Nadia", restored.DisplayName);
        Assert.Equal("Efficient", restored.TravelStyle);
        Assert.Equal("Night Owl", restored.EnergyPattern);
        Assert.Equal("Splurge Occasionally", restored.BudgetComfort);
        Assert.Equal("Minimize Walking", restored.Mobility);
        Assert.Equal("Couple", restored.TravelGroup);
        Assert.Equal(3, restored.SpiceTolerance);
        Assert.False(restored.FirstTimeVisitor);
        Assert.Contains("Vegan", restored.DietaryRestrictions);
        Assert.Contains("Art", restored.CoreInterests);
        Assert.Contains("Tourist Traps", restored.HardAvoids);
        Assert.Contains("Thai", restored.FavoriteCuisines);
        Assert.Contains("Street Food", restored.DiningVibe);
    }

    [Fact]
    public void From_FallsBackToAgentFacingFieldsForProfilesSavedBeforeOnboarding()
    {
        // TestData.Profile carries the pre-onboarding shape: dietary rules / interests / avoid list only.
        var restored = TravelerOnboardingDraft.From(TestData.Profile);

        Assert.Contains("Vegetarian", restored.DietaryRestrictions);
        Assert.Contains("old town souks", restored.CoreInterests);
        Assert.Contains("crowded mega malls", restored.HardAvoids);
        Assert.Equal("Balanced", restored.TravelStyle);
        Assert.Equal("Mid-Range", restored.BudgetComfort);
    }
}
