using MeridianAgent.Domain;
using MeridianAgent.Domain.Entities;
using MeridianAgent.Infrastructure.Data;
using MeridianAgent.Infrastructure.External;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MeridianAgent.Tests;

public class FileStoreTests
{
    private static string RepoSamplesDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "samples");
                if (Directory.Exists(Path.Combine(candidate, "rulebooks")))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not locate the repository samples directory.");
        }
    }

    private static StorageOptions Storage(string directory) => new() { DataDirectory = directory };

    [Fact]
    public async Task RulebookStore_LoadsSampleKyotoRulebook()
    {
        var store = new JsonFileRulebookStore(
            Options.Create(Storage(RepoSamplesDirectory)),
            NullLogger<JsonFileRulebookStore>.Instance);

        var rulebooks = await store.ListAsync();

        var kyoto = Assert.Single(rulebooks);
        Assert.Equal("Kyoto, Japan", kyoto.Destination);
        Assert.Equal(5, kyoto.Rules.Count);
        Assert.Contains(kyoto.Rules, rule => rule.Category == RuleCategory.Photography);
    }

    [Fact]
    public async Task RulebookStore_GetAsync_FindsByDestination()
    {
        var store = new JsonFileRulebookStore(
            Options.Create(Storage(RepoSamplesDirectory)),
            NullLogger<JsonFileRulebookStore>.Instance);

        var rulebook = await store.GetAsync("Kyoto, Japan");

        Assert.NotNull(rulebook);
        Assert.NotEmpty(rulebook!.Rules);
    }

    [Fact]
    public async Task UserProfileStore_LoadsDefaultProfile()
    {
        var store = new JsonFileUserProfileStore(
            Options.Create(Storage(RepoSamplesDirectory)),
            NullLogger<JsonFileUserProfileStore>.Instance);

        var profile = await store.GetAsync("default");

        Assert.NotNull(profile);
        Assert.Equal("Aria", profile!.DisplayName);
        Assert.Equal("Kyoto, Japan", profile.Destination);
        Assert.Equal(TravelerProfile.FirstTime, profile.TravelerProfile);
        Assert.Contains("vegetarian", profile.DietaryRules);
    }

    [Fact]
    public async Task UserProfileStore_SaveAndReloadRoundTrips()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-tests", Guid.NewGuid().ToString("n"));
        var store = new JsonFileUserProfileStore(
            Options.Create(Storage(directory)),
            NullLogger<JsonFileUserProfileStore>.Instance);

        var saved = TestData.Profile with { Id = "roundtrip" };
        await store.SaveAsync(saved);

        var reloaded = await store.GetAsync("roundtrip");

        Assert.NotNull(reloaded);
        Assert.Equal("Aria", reloaded!.DisplayName);
        Assert.Contains("temples", reloaded.Interests);

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task NearbyVenueStore_FallsBackToSharedVenueList()
    {
        var store = new JsonFileNearbyVenueStore(
            Options.Create(Storage(RepoSamplesDirectory)),
            NullLogger<JsonFileNearbyVenueStore>.Instance);

        var venues = await store.GetAsync("Kyoto, Japan");

        Assert.NotEmpty(venues);
        Assert.Contains(venues, venue => venue.Name == "Ippodo Tea Room");
    }

    [Fact]
    public async Task TravelContextProvider_FallsBackToSampleContextWhenServicesUnavailable()
    {
        var nominatim = new NominatimClient(
            new HttpClient(),
            Options.Create(new NominatimOptions { BaseUrl = "http://127.0.0.1:1" }));

        var weather = new WeatherClient(
            new HttpClient(),
            Options.Create(new WeatherOptions { ApiKey = string.Empty }));

        var provider = new LiveTravelContextProvider(
            nominatim,
            weather,
            Options.Create(Storage(RepoSamplesDirectory)),
            NullLogger<LiveTravelContextProvider>.Instance);

        var context = await provider.GetCurrentAsync("Kyoto, Japan");

        Assert.Equal("Kyoto, Japan", context.Location);
        Assert.Equal("21:40", context.LocalTime);
        Assert.True(context.IsRaining);
    }
}

public class DataPathsTests
{
    [Fact]
    public void Resolve_ReturnsRootedPathsUnchanged()
    {
        Assert.Equal("/data/samples", DataPaths.Resolve("/data/samples"));
    }

    [Fact]
    public void Resolve_FallsBackToCurrentDirectory()
    {
        var expected = Path.Combine(Directory.GetCurrentDirectory(), "definitely-missing-samples-dir");

        Assert.Equal(expected, DataPaths.Resolve("definitely-missing-samples-dir"));
    }
}
