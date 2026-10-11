namespace MeridianAgent.Tests;

using MeridianAgent.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

public class UserProfileServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly string _databasePath;

    public UserProfileServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "meridian-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "profiles.db");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Windows/CI can hold the file a moment longer; nothing to clean up after a test run.
        }
    }

    private UserProfileService CreateService()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;

        var factory = new TestDbContextFactory(options);
        var service = new UserProfileService(factory, NullLogger<UserProfileService>.Instance);

        using var context = factory.CreateDbContext();
        context.Database.Migrate();

        return service;
    }

    [Fact]
    public async Task HasCompletedProfileAsync_IsFalseWhenNothingIsStored()
    {
        var service = CreateService();

        Assert.False(await service.HasCompletedProfileAsync());
    }

    [Fact]
    public async Task HasCompletedProfileAsync_IsFalseForAProfileSavedWithoutOnboarding()
    {
        // Legacy travelers: a profile row that never went through the wizard.
        var service = CreateService();
        await service.SaveProfileAsync(UserProfile.Default with { DisplayName = "Aria" });

        Assert.False(await service.HasCompletedProfileAsync());
    }

    [Fact]
    public async Task SaveProfileAsync_ThenHasCompletedProfileAsync_IsTrue()
    {
        var service = CreateService();
        await service.SaveProfileAsync(CompletedProfile());

        Assert.True(await service.HasCompletedProfileAsync());
    }

    [Fact]
    public async Task SaveProfileAsync_RoundTripsEveryOnboardingPreference()
    {
        var service = CreateService();
        var saved = CompletedProfile();

        await service.SaveProfileAsync(saved);

        var reloaded = await service.GetProfileAsync();

        Assert.NotNull(reloaded);
        Assert.Equal("Nadia", reloaded!.DisplayName);
        Assert.Equal("Slow Explorer", reloaded.TravelStyle);
        Assert.Equal("Night Owl", reloaded.EnergyPattern);
        Assert.Equal("Splurge Occasionally", reloaded.BudgetComfort);
        Assert.Equal("Minimize Walking", reloaded.Mobility);
        Assert.Equal("Couple", reloaded.TravelGroup);
        Assert.Equal(3, reloaded.SpiceTolerance);
        Assert.False(reloaded.FirstTimeVisitor);

        Assert.Equal(new[] { "Vegetarian", "Halal" }, reloaded.DietaryRestrictions);
        Assert.Equal(new[] { "Vegetarian", "Halal" }, reloaded.DietaryRules);
        Assert.Equal(new[] { "Architecture" }, reloaded.CoreInterests);
        Assert.Equal(new[] { "Architecture" }, reloaded.Interests);
        Assert.Equal(new[] { "Crowded Places" }, reloaded.HardAvoids);
        Assert.Equal(new[] { "Crowded Places" }, reloaded.AvoidList);
        Assert.Equal(new[] { "Thai" }, reloaded.FavoriteCuisines);
        Assert.Equal(new[] { "Street Food" }, reloaded.DiningVibe);

        Assert.Equal(PacingPreference.Slow, reloaded.Pacing);
        Assert.Equal(BudgetRange.Medium, reloaded.Budget);
        Assert.Equal(GroupComposition.Couple, reloaded.GroupComposition);
        Assert.Equal(TravelerProfile.Experienced, reloaded.TravelerProfile);
        Assert.Equal("Dubai, UAE", reloaded.Destination);
        Assert.True(reloaded.OnboardingCompleted);
        Assert.True(reloaded.PendingWelcome);
    }

    [Fact]
    public async Task SaveProfileAsync_ReplacesExistingValuesRatherThanAppending()
    {
        var service = CreateService();
        await service.SaveProfileAsync(CompletedProfile());

        // The traveler drops one interest and adds another: the row must follow, not accumulate.
        var edited = CompletedProfile() with
        {
            CoreInterests = new[] { "Art" },
            Interests = new[] { "Art" },
            SpiceTolerance = 5,
        };
        await service.SaveProfileAsync(edited);

        var reloaded = await service.GetProfileAsync();

        Assert.NotNull(reloaded);
        Assert.Single(reloaded!.CoreInterests, "Art");
        Assert.Equal(5, reloaded.SpiceTolerance);
        Assert.Equal(1, await CountProfilesAsync(service));
    }

    [Fact]
    public async Task GetOrCreateAsync_ReturnsDefaultWhenNothingIsStored()
    {
        var service = CreateService();

        var profile = await service.GetOrCreateAsync();

        Assert.Equal("Traveler", profile.DisplayName);
        Assert.Equal("default", profile.Id);
        Assert.False(profile.OnboardingCompleted);
    }

    [Fact]
    public async Task SaveProfileAsync_WritesAnEmptyJsonArrayForANullList()
    {
        // The converter used to write the four characters "null" into a list column, which is not
        // valid JSON for a reader that does not use the converter and breaks JSON SQL against the row.
        var service = CreateService();
        await service.SaveProfileAsync(UserProfile.Default with
        {
            CoreInterests = null!,
            Interests = null!,
            HardAvoids = null!,
        });

        var stored = await ReadColumnAsync("core_interests_json");

        Assert.Equal("[]", stored);
    }

    [Fact]
    public async Task GetProfileAsync_ReadsALegacyRowWhoseListColumnsAreUnreadable()
    {
        // The columns are NOT NULL, so the shapes a legacy row can actually hold are the converter's
        // old "null" string and a corrupted cell. Both must still load as a usable profile.
        var service = CreateService();
        await service.SaveProfileAsync(UserProfile.Default);
        await WriteColumnsAsync(
            ("core_interests_json", "null"),
            ("interests_json", "{not json"));

        var profile = await service.GetProfileAsync();

        Assert.NotNull(profile);
        Assert.Empty(profile!.CoreInterests);
        Assert.Empty(profile.Interests);
        Assert.Empty(profile.DietaryRestrictions);
        Assert.Empty(profile.AvoidList);
    }

    [Fact]
    public async Task MarkWelcomeSeenAsync_ClearsOnlyTheFlag()
    {
        var service = CreateService();
        await service.SaveProfileAsync(CompletedProfile());

        await service.MarkWelcomeSeenAsync();

        var after = await service.GetProfileAsync();
        Assert.NotNull(after);
        Assert.False(after!.PendingWelcome);
        Assert.True(after.OnboardingCompleted);
        Assert.Equal("Nadia", after.DisplayName);
        Assert.True(await service.HasCompletedProfileAsync());
    }

    [Fact]
    public async Task MarkWelcomeSeenAsync_IsHarmlessWithoutAProfileOrPendingWelcome()
    {
        var service = CreateService();

        await service.MarkWelcomeSeenAsync();
        await service.SaveProfileAsync(UserProfile.Default);
        await service.MarkWelcomeSeenAsync();

        var profile = await service.GetProfileAsync();
        Assert.NotNull(profile);
        Assert.False(profile!.PendingWelcome);
    }

    [Fact]
    public async Task ProfileDatabaseInitializer_ImportsProfilesFromLegacyJson()
    {
        var legacyDirectory = Path.Combine(_directory, "legacy");
        var profilesDirectory = Path.Combine(legacyDirectory, "profiles");
        Directory.CreateDirectory(profilesDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(profilesDirectory, "default.json"),
            """
            {
              "id": "default",
              "display_name": "Aria",
              "destination": "Dubai, UAE",
              "dietary_rules": ["vegetarian"],
              "interests": ["old town souks"],
              "avoid_list": ["crowded mega malls"],
              "pacing": "moderate",
              "budget": "medium",
              "traveler_profile": "first_time",
              "group_composition": "solo",
              "onboarding_completed": true
            }
            """);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;

        var factory = new TestDbContextFactory(options);
        var legacyStore = new JsonFileUserProfileStore(
            Microsoft.Extensions.Options.Options.Create(new StorageOptions { DataDirectory = legacyDirectory }),
            NullLogger<JsonFileUserProfileStore>.Instance);

        var initializer = new ProfileDatabaseInitializer(
            factory,
            legacyStore,
            NullLogger<ProfileDatabaseInitializer>.Instance);

        await initializer.InitializeAsync();

        var service = new UserProfileService(factory, NullLogger<UserProfileService>.Instance);
        var profile = await service.GetProfileAsync();

        Assert.NotNull(profile);
        Assert.Equal("Aria", profile!.DisplayName);
        Assert.True(profile.OnboardingCompleted);
        Assert.Equal(new[] { "old town souks" }, profile.Interests);
    }

    private async Task<int> CountProfilesAsync(UserProfileService service)
    {
        // One context instance reads the whole table: the service API never leaves stray rows.
        using var context = new TestDbContextFactory(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_databasePath}").Options)
            .CreateDbContext();

        return await context.Profiles.CountAsync();
    }

    private async Task<string?> ReadColumnAsync(string column)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM Profiles LIMIT 1";
        return await command.ExecuteScalarAsync() as string;
    }

    private async Task WriteColumnsAsync(params (string Column, object Value)[] columns)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        // Column names are compile-time constants from the test; the values are parameterized.
        foreach (var (column, value) in columns)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE Profiles SET {column} = $value";
            command.Parameters.AddWithValue("$value", value);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static UserProfile CompletedProfile()
    {
        var draft = new TravelerOnboardingDraft
        {
            DisplayName = "Nadia",
            TravelStyle = "Slow Explorer",
            EnergyPattern = "Night Owl",
            BudgetComfort = "Splurge Occasionally",
            Mobility = "Minimize Walking",
            TravelGroup = "Couple",
            SpiceTolerance = 3,
            FirstTimeVisitor = false,
        };
        TravelerOnboardingDraft.Toggle(draft.DietaryRestrictions, "Vegetarian");
        TravelerOnboardingDraft.Toggle(draft.DietaryRestrictions, "Halal");
        TravelerOnboardingDraft.Toggle(draft.CoreInterests, "Architecture");
        TravelerOnboardingDraft.Toggle(draft.HardAvoids, "Crowded Places");
        TravelerOnboardingDraft.Toggle(draft.FavoriteCuisines, "Thai");
        TravelerOnboardingDraft.Toggle(draft.DiningVibe, "Street Food");

        return draft.BuildProfile(
            IUserProfileService.DefaultProfileId,
            "Dubai, UAE",
            null,
            null,
            onboardingCompleted: true,
            pendingWelcome: true);
    }

    /// <summary>Hands out contexts for one connection string, standing in for the DI factory.</summary>
    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;

        public AppDbContext CreateDbContext() => new(_options);
    }
}
