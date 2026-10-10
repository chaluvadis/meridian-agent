namespace MeridianAgent.Tests;

public class UserProfileServiceTests
{
    private static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static UserProfileService CreateService(string directory) => new(
        new JsonFileUserProfileStore(
            Options.Create(new StorageOptions { DataDirectory = directory }),
            NullLogger<JsonFileUserProfileStore>.Instance),
        NullLogger<UserProfileService>.Instance);

    [Fact]
    public async Task HasCompletedProfileAsync_IsFalseWhenNothingIsStored()
    {
        var service = CreateService(TempDirectory());

        Assert.False(await service.HasCompletedProfileAsync());
    }

    [Fact]
    public async Task HasCompletedProfileAsync_IsFalseForAProfileSavedWithoutOnboarding()
    {
        // The sample profile ships with the app but has never been through onboarding.
        var directory = TempDirectory();
        var service = CreateService(directory);
        await service.SaveProfileAsync(UserProfile.Default with { Id = "default", DisplayName = "Aria" });

        Assert.False(await service.HasCompletedProfileAsync());
    }

    [Fact]
    public async Task SaveProfileAsync_ThenHasCompletedProfileAsync_IsTrue()
    {
        var directory = TempDirectory();
        var service = CreateService(directory);

        var profile = TravelerOnboardingDraftRoundTrip(service);
        await service.SaveProfileAsync(profile);

        Assert.True(await service.HasCompletedProfileAsync());
    }

    [Fact]
    public async Task SaveProfileAsync_RoundTripsOnboardingPreferences()
    {
        var directory = TempDirectory();
        var service = CreateService(directory);
        var saved = TravelerOnboardingDraftRoundTrip(service);
        await service.SaveProfileAsync(saved);

        var reloaded = await service.GetProfileAsync();

        Assert.NotNull(reloaded);
        Assert.Equal("Nadia", reloaded!.DisplayName);
        Assert.Equal("Slow Explorer", reloaded.TravelStyle);
        Assert.Equal(3, reloaded.SpiceTolerance);
        Assert.Contains("Vegetarian", reloaded.DietaryRestrictions);
        Assert.Contains("Halal", reloaded.DietaryRules);
        Assert.Contains("Architecture", reloaded.CoreInterests);
        Assert.Contains("Architecture", reloaded.Interests);
        Assert.True(reloaded.OnboardingCompleted);
        Assert.True(reloaded.PendingWelcome);
    }

    [Fact]
    public async Task GetOrCreateAsync_ReturnsDefaultWhenNothingIsStored()
    {
        var service = CreateService(TempDirectory());

        var profile = await service.GetOrCreateAsync();

        Assert.Equal("Traveler", profile.DisplayName);
        Assert.False(profile.OnboardingCompleted);
    }

    [Fact]
    public async Task MarkWelcomeSeenAsync_OnlyClearsTheFlag()
    {
        var directory = TempDirectory();
        var service = CreateService(directory);
        await service.SaveProfileAsync(TravelerOnboardingDraftRoundTrip(service));

        await service.MarkWelcomeSeenAsync();

        var after = await service.GetProfileAsync();
        Assert.NotNull(after);
        Assert.False(after!.PendingWelcome);
        Assert.True(after.OnboardingCompleted);
        Assert.Equal("Nadia", after.DisplayName);
        Assert.True(await service.HasCompletedProfileAsync());
    }

    [Fact]
    public async Task MarkWelcomeSeenAsync_IsHarmlessWhenNoProfileOrNoPendingWelcome()
    {
        var directory = TempDirectory();
        var service = CreateService(directory);

        await service.MarkWelcomeSeenAsync(); // no profile at all
        await service.SaveProfileAsync(UserProfile.Default); // no pending welcome
        await service.MarkWelcomeSeenAsync();

        var profile = await service.GetProfileAsync();
        Assert.NotNull(profile);
        Assert.False(profile!.PendingWelcome);
    }

    private static UserProfile TravelerOnboardingDraftRoundTrip(UserProfileService service)
    {
        var draft = new TravelerOnboardingDraft
        {
            DisplayName = "Nadia",
            TravelStyle = "Slow Explorer",
            BudgetComfort = "Money No Object",
            TravelGroup = "Couple",
            SpiceTolerance = 3,
        };
        TravelerOnboardingDraft.Toggle(draft.DietaryRestrictions, "Vegetarian");
        TravelerOnboardingDraft.Toggle(draft.DietaryRestrictions, "Halal");
        TravelerOnboardingDraft.Toggle(draft.CoreInterests, "Architecture");

        return draft.BuildProfile(
            IUserProfileService.DefaultProfileId,
            "Dubai, UAE",
            "Downtown Dubai",
            null,
            onboardingCompleted: true,
            pendingWelcome: true);
    }
}
