namespace MeridianAgent.Tests;

using System.Net;

using MeridianAgent.Application.Abstractions;

using MeridianAgent.Domain.AgentContracts;
using MeridianAgent.Domain.Entities;

using MeridianAgent.Infrastructure.Data;
using MeridianAgent.Infrastructure.External;

/// <summary>
/// A position is only worth what can be measured with it. These are the measurements: distance
/// between two points, a route's minutes, and the venues that turn out to be genuinely around a
/// traveler rather than merely listed near a city.
/// </summary>
public class GeoPointTests
{
    private static readonly GeoPoint DowntownDubai = new(25.2048, 55.2708);

    private static readonly GeoPoint DubaiMarina = new(25.0800, 55.1400);

    [Fact]
    public void DistanceIsStraightLineBetweenTwoPoints()
    {
        // Dubai Marina is about 19km south-west of Downtown Dubai, cross-checked against a flat
        // approximation: 13.9km of latitude and 11.8km of longitude at this latitude.
        Assert.InRange(DowntownDubai.DistanceKmTo(DubaiMarina), 18.5, 19.8);
    }

    [Fact]
    public void APointIsZeroKilometresFromItself()
    {
        Assert.Equal(0, DowntownDubai.DistanceKmTo(DowntownDubai), 6);
    }

    [Fact]
    public void DistanceIsTheSameInBothDirections()
    {
        Assert.Equal(
            DowntownDubai.DistanceKmTo(DubaiMarina),
            DubaiMarina.DistanceKmTo(DowntownDubai),
            6);
    }

    [Fact]
    public void ACoordinateInSecondsDecimalsStillCountsAsNearby()
    {
        var almostThere = new GeoPoint(25.2049, 55.2709);

        Assert.True(DowntownDubai.DistanceKmTo(almostThere) > 0);
        Assert.True(DowntownDubai.DistanceKmTo(almostThere) < 0.5);
    }

    [Fact]
    public void NullIslandIsNotAPlaceAnyoneIs()
    {
        Assert.False(new GeoPoint(0, 0).IsPlausible);
    }

    [Theory]
    [InlineData(25.2048, 55.2708)]
    [InlineData(-33.9, 151.2)]
    [InlineData(51.5, -0.1)]
    public void RealPlacesArePlausible(double latitude, double longitude)
    {
        Assert.True(new GeoPoint(latitude, longitude).IsPlausible);
    }
}

public class OsrmTravelClientTests
{
    private static OsrmTravelClient CreateClient(RecordingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") },
            Microsoft.Extensions.Options.Options.Create(new OsrmOptions()),
            NullLogger<OsrmTravelClient>.Instance);

    private const string Route = """
        {"code":"Ok","routes":[{"duration":612.4,"distance":2401.2}]}
        """;

    [Fact]
    public async Task MinutesAreRoundedUpFromTheRouteDuration()
    {
        var handler = new RecordingHandler(Route);
        var client = CreateClient(handler);

        var minutes = await client.MinutesAsync(new GeoPoint(25.2048, 55.2708), new GeoPoint(25.2200, 55.2800));

        Assert.Equal(10, minutes);

        // OSRM takes longitude first, and origin then destination.
        Assert.Contains("/route/v1/driving/", handler.Path);
        Assert.Contains("55.28,25.22", handler.Path);
    }

    [Fact]
    public async Task NoRouteComesBackAsNullRatherThanAZero()
    {
        var handler = new RecordingHandler("""{"code":"NoRoute","routes":[]}""");
        var client = CreateClient(handler);

        Assert.Null(await client.MinutesAsync(new GeoPoint(25.2, 55.2), new GeoPoint(24.0, 54.0)));
    }

    [Fact]
    public async Task AServiceThatFailsKeepsTheCallersFallback()
    {
        var handler = new RecordingHandler("{}", HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        Assert.Null(await client.MinutesAsync(new GeoPoint(25.2, 55.2), new GeoPoint(25.3, 55.3)));
    }

    [Fact]
    public async Task APositionNobodyHasMeansNoRouteToAskFor()
    {
        var handler = new RecordingHandler(Route);
        var client = CreateClient(handler);

        Assert.Null(await client.MinutesAsync(new GeoPoint(0, 0), new GeoPoint(25.2, 55.2)));
        Assert.Equal(0, handler.Calls);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;

        public RecordingHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        public string? Path { get; private set; }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Path = request.RequestUri!.AbsolutePath;

            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body),
            });
        }
    }
}

public class OverpassVenueSourceTests
{
    private const string AroundOneVenue = """
        {"elements":[
          {"type":"node","id":1,"lat":25.2049,"lon":55.2709,
           "tags":{"name":"Karak House","amenity":"cafe"}}
        ]}
        """;

    private static OverpassVenueSource CreateSource(RecordingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api") },
            Microsoft.Extensions.Options.Options.Create(new OverpassOptions()),
            NullLogger<OverpassVenueSource>.Instance);

    [Fact]
    public async Task VenuesComeBackWithThePositionTheyWereFoundAt()
    {
        var source = CreateSource(new RecordingHandler(AroundOneVenue));

        var venues = await source.GetAroundAsync(new GeoPoint(25.2048, 55.2708));

        var venue = Assert.Single(venues);
        Assert.Equal("Karak House", venue.Name);
        Assert.Equal("cafe", venue.Category);
        Assert.True(venue.HasCoordinates);

        // The distance is a measurement now, not a claim.
        Assert.True(venue.DistanceKmFrom(new GeoPoint(25.2048, 55.2708)) < 0.2);
    }

    [Fact]
    public async Task VenuesAreListedNearestFirst()
    {
        var source = CreateSource(new RecordingHandler("""
            {"elements":[
              {"lat":25.2400,"lon":55.3000,"tags":{"name":"Far One","amenity":"restaurant"}},
              {"lat":25.2049,"lon":55.2709,"tags":{"name":"Near One","amenity":"cafe"}}
            ]}
            """));

        var venues = await source.GetAroundAsync(new GeoPoint(25.2048, 55.2708));

        Assert.Equal(new[] { "Near One", "Far One" }, venues.Select(v => v.Name));
    }

    [Fact]
    public async Task NothingWithoutANameIsAVenue()
    {
        var source = CreateSource(new RecordingHandler("""
            {"elements":[{"lat":25.2,"lon":55.2,"tags":{"amenity":"cafe"}}]}
            """));

        Assert.Empty(await source.GetAroundAsync(new GeoPoint(25.2048, 55.2708)));
    }

    [Fact]
    public async Task AFailingServiceIsAnEmptyListNotAnError()
    {
        var source = CreateSource(new RecordingHandler("boom", HttpStatusCode.ServiceUnavailable));

        Assert.Empty(await source.GetAroundAsync(new GeoPoint(25.2048, 55.2708)));
    }

    [Fact]
    public async Task AnUnconfiguredSourceNeverAsks()
    {
        var handler = new RecordingHandler(AroundOneVenue);

        var source = new OverpassVenueSource(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api") },
            Microsoft.Extensions.Options.Options.Create(new OverpassOptions { Enabled = false }),
            NullLogger<OverpassVenueSource>.Instance);

        Assert.Empty(await source.GetAroundAsync(new GeoPoint(25.2048, 55.2708)));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task TheSameSpotIsAskedForOnce()
    {
        var handler = new RecordingHandler(AroundOneVenue);
        var source = CreateSource(handler);

        await source.GetAroundAsync(new GeoPoint(25.2048, 55.2708));
        await source.GetAroundAsync(new GeoPoint(25.2048, 55.2708));
        await source.GetAroundAsync(new GeoPoint(25.2049, 55.2709));

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void TheQueryAsksForPlacesAndNothingElse()
    {
        var source = CreateSource(new RecordingHandler("{}"));
        var query = InvokeBuildQuery(source, new GeoPoint(25.2048, 55.2708));

        Assert.Contains("around:1500,25.2048,55.2708", query);
        Assert.Contains("[\"name\"]", query);
        Assert.Contains("amenity", query);
        Assert.Contains("tourism", query);
        Assert.DoesNotContain("highway", query);
    }

    private static string InvokeBuildQuery(OverpassVenueSource source, GeoPoint centre)
    {
        var method = typeof(OverpassVenueSource)
            .GetMethod("BuildQuery", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        return (string)method.Invoke(source, new object[] { centre })!;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;

        public RecordingHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body),
            });
        }
    }
}

/// <summary>
/// The store's job is choosing the honest source: real venues around a shared position, and the
/// sample file when there is nothing better. Getting that wrong in either direction looks fine —
/// either it always asks a rate-limited service, or it quietly ignores the position.
/// </summary>
public class CompositeNearbyVenueStoreTests
{
    private static readonly GeoPoint Downtown = new(25.2048, 55.2708);

    private static CompositeNearbyVenueStore CreateStore(
        IReadOnlyList<NearbyVenue>? fileVenues,
        IReadOnlyList<NearbyVenue>? liveVenues,
        ITravelTimeProvider travelTime)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-venues", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);

        if (fileVenues is not null)
        {
            File.WriteAllText(
                Path.Combine(directory, "nearby_venues.json"),
                System.Text.Json.JsonSerializer.Serialize(
                    fileVenues,
                    new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower }));
        }

        return new CompositeNearbyVenueStore(
            new JsonFileNearbyVenueStore(
                Microsoft.Extensions.Options.Options.Create(new StorageOptions { DataDirectory = directory }),
                NullLogger<JsonFileNearbyVenueStore>.Instance),
            new StubLiveSource(liveVenues),
            travelTime,
            NullLogger<CompositeNearbyVenueStore>.Instance);
    }

    [Fact]
    public async Task ASharedPositionGetsLiveVenues()
    {
        var store = CreateStore(
            fileVenues: Array.Empty<NearbyVenue>(),
            liveVenues: new[] { new NearbyVenue { Name = "Karak House", IsIndoor = true } },
            new StubTravelTime(null));

        var venues = await store.GetAsync("Downtown Dubai", Downtown);

        Assert.Equal("Karak House", Assert.Single(venues).Name);
    }

    [Fact]
    public async Task NoPositionKeepsTheFileList()
    {
        var store = CreateStore(
            fileVenues: new[] { new NearbyVenue { Name = "Sample Venue" } },
            liveVenues: new[] { new NearbyVenue { Name = "Karak House" } },
            new StubTravelTime(null));

        var venues = await store.GetAsync("Downtown Dubai");

        Assert.Equal("Sample Venue", Assert.Single(venues).Name);
    }

    [Fact]
    public async Task ALiveSourceWithNoAnswerFallsBackToTheFile()
    {
        var store = CreateStore(
            fileVenues: new[] { new NearbyVenue { Name = "Sample Venue" } },
            liveVenues: Array.Empty<NearbyVenue>(),
            new StubTravelTime(null));

        var venues = await store.GetAsync("Downtown Dubai", Downtown);

        Assert.Equal("Sample Venue", Assert.Single(venues).Name);
    }

    [Fact]
    public async Task LivingVenuesGetTheirDistancesMeasured()
    {
        var live = new[]
        {
            new NearbyVenue
            {
                Name = "Near",
                IsIndoor = true,
                Latitude = 25.2050,
                Longitude = 55.2710,
                DistanceMinutes = 99,
            },
            new NearbyVenue
            {
                Name = "Far",
                IsIndoor = true,
                Latitude = 25.3000,
                Longitude = 55.4000,
                DistanceMinutes = 2,
            },
        };

        var store = CreateStore(Array.Empty<NearbyVenue>(), live, new StubTravelTime(7));

        var venues = await store.GetAsync("Downtown Dubai", Downtown);

        // A measured figure replaces the placeholder, whichever way it points.
        Assert.All(venues, venue => Assert.Equal(7, venue.DistanceMinutes));
    }

    [Fact]
    public async Task AnUnroutableVenueKeepsWhatItHadRatherThanBeingDropped()
    {
        var live = new[]
        {
            new NearbyVenue
            {
                Name = "Across the creek",
                IsIndoor = true,
                Latitude = 25.2050,
                Longitude = 55.2710,
                DistanceMinutes = 12,
            },
        };

        var store = CreateStore(Array.Empty<NearbyVenue>(), live, new StubTravelTime(null));

        var venues = await store.GetAsync("Downtown Dubai", Downtown);

        Assert.Equal(12, Assert.Single(venues).DistanceMinutes);
    }

    private sealed class StubLiveSource : ILiveVenueSource
    {
        private readonly IReadOnlyList<NearbyVenue>? _venues;

        public StubLiveSource(IReadOnlyList<NearbyVenue>? venues) => _venues = venues;

        public bool Enabled { get; init; } = true;

        public bool IsEnabled => Enabled;

        public bool Used { get; private set; }

        public GeoPoint? AskedAround { get; private set; }

        public Task<IReadOnlyList<NearbyVenue>> GetAroundAsync(
            GeoPoint centre,
            CancellationToken cancellationToken = default)
        {
            if (_venues is null)
            {
                return Task.FromResult<IReadOnlyList<NearbyVenue>>(Array.Empty<NearbyVenue>());
            }

            Used = true;
            AskedAround = centre;
            return Task.FromResult(_venues);
        }
    }

    private sealed class StubTravelTime : ITravelTimeProvider
    {
        private readonly int? _minutes;

        public StubTravelTime(int? minutes) => _minutes = minutes;

        public bool IsConfigured => _minutes is not null;

        public Task<int?> MinutesAsync(GeoPoint from, GeoPoint to, CancellationToken cancellationToken = default) =>
            Task.FromResult(_minutes);
    }

    private sealed class NoopHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The stub answers before any request is made");
    }
}

/// <summary>
/// A traveler who lets the app find them is no longer a city name: the position is what everything
/// downstream measures against, and it has to reach each of the four places that ask.
/// </summary>
public class TravelerPositionTests
{
    [Fact]
    public void ThePositionIsOptionalAndEmptyUntilItIsShared()
    {
        var profile = UserProfile.Default with { Destination = "Dubai, UAE" };

        Assert.Null(profile.Coordinates);
        Assert.False(profile.HasCoordinates);
        Assert.Equal("Dubai, UAE", profile.EffectiveLocation);
    }

    [Fact]
    public void ASharedPositionIsWhatGetsMeasuredAgainst()
    {
        var profile = UserProfile.Default with
        {
            HomeBase = "Downtown Dubai",
            Latitude = 25.2048,
            Longitude = 55.2708,
        };

        Assert.True(profile.HasCoordinates);

        var point = profile.Coordinates;
        Assert.NotNull(point);
        Assert.Equal(25.2048, point!.Latitude);
        Assert.Equal(55.2708, point.Longitude);

        // The name is unchanged: the position adds to it rather than replacing what they typed.
        Assert.Equal("Downtown Dubai", profile.EffectiveLocation);
    }

    [Fact]
    public void AProfileRoundTripsItsPosition()
    {
        var located = UserProfile.Default with { Latitude = 25.2, Longitude = 55.3 };

        var json = System.Text.Json.JsonSerializer.Serialize(located);
        using var document = System.Text.Json.JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("latitude", out var latitude));
        Assert.Equal(25.2, latitude.GetDouble());
    }

    [Fact]
    public void AContextCarriesThePositionItWasReadAt()
    {
        var context = new TravelContext
        {
            Location = "Downtown Dubai",
            WeatherCondition = "Clear",
            Latitude = 25.2048,
            Longitude = 55.2708,
        };

        Assert.NotNull(context.At);
        Assert.Equal(25.2048, context.At!.Latitude);
    }

    [Fact]
    public void AContextWithOnlyACityNameHasNoPosition()
    {
        var context = new TravelContext { Location = "Dubai, UAE" };

        Assert.Null(context.At);
    }

    [Fact]
    public async Task TheWeatherPivotSendsThePositionAlongWithTheCityName()
    {
        // The one place that measures the weather: it must ask about where the traveler is, not
        // the centre of the city they typed, or the pivot warns about the wrong rain.
        var context = new StubTravelContextProvider
        {
            Context = new TravelContext
            {
                Location = "Downtown Dubai",
                LocalTime = "20:45",
                WeatherCondition = "Rain",
                TemperatureC = 29,
                IsRaining = true,
            },
        };
        var venues = new StubNearbyVenueStore();

        var profiles = new StubUserProfileService
        {
            Profile = UserProfile.Default with
            {
                HomeBase = "Downtown Dubai",
                Latitude = 25.2048,
                Longitude = 55.2708,
            },
        };

        var pivots = new WeatherPivotService(
            new StubScopeFactory(new StubServiceProvider(new Dictionary<Type, object>
            {
                [typeof(IProactiveSuggestionAgent)] = new FakeProactiveSuggestionAgent(),
                [typeof(ISynthesizerAgent)] = new FakeSynthesizerAgent(),
                [typeof(IUserProfileService)] = profiles,
                [typeof(IRulebookStore)] = new StubRulebookStore(),
                [typeof(INearbyVenueStore)] = venues,
                [typeof(ITravelContextProvider)] = context,
                [typeof(IChatHistoryService)] = new StubChatHistoryService
                {
                    FavoritesForToday = new[]
                    {
                        new ChatMessageEntity
                        {
                            Id = 1,
                            Content = "Top pick: Al Fahidi Heritage Walk.",
                            Timestamp = DateTime.Now.AddHours(-1),
                            IsFavorite = true,
                            RichContentJson = "{\"blocks\":[{\"kind\":3,\"text\":\"Al Fahidi Heritage Walk\",\"detail\":\"Activity \u00b7 Outdoor \u00b7 10 min away\"}]}",
                        },
                    },
                },
            })),
            new RecordingChatNotifier(),
            new SignatureCooldown(TimeSpan.FromHours(4)),
            Microsoft.Extensions.Options.Options.Create(new WeatherPivotOptions()),
            NullLogger<WeatherPivotService>.Instance);

        Assert.True(await pivots.TryPublishPivotAsync(CancellationToken.None));

        Assert.NotNull(context.LastPosition);
        Assert.Equal(25.2048, context.LastPosition!.Latitude);
        Assert.NotNull(venues.LastPosition);
        Assert.Equal(25.2048, venues.LastPosition!.Latitude);
    }
}


/// <summary>
/// A base URL with a path in it is where relative resolution bites. Posting "interpreter" against
/// ".../api" resolves to ".../interpreter" - the path segment is replaced, not extended - which
/// answers the wrong endpoint and silently returns nothing. The URL is built absolutely now.
/// </summary>
public class OverpassUrlTests
{
    [Theory]
    [InlineData("http://localhost/api", "http://localhost/api/interpreter")]
    [InlineData("http://localhost/api/", "http://localhost/api/interpreter")]
    [InlineData("https://overpass-api.de/api", "https://overpass-api.de/api/interpreter")]
    [InlineData("http://overpass:80/api", "http://overpass:80/api/interpreter")]
    public void TheInterpreterUrlKeepsWhateverPathTheBaseUrlHas(string baseUrl, string expected)
    {
        var source = new OverpassVenueSource(
            new HttpClient(new NoopHandler()),
            Microsoft.Extensions.Options.Options.Create(new OverpassOptions { BaseUrl = baseUrl }),
            NullLogger<OverpassVenueSource>.Instance);

        var url = (string)typeof(OverpassVenueSource)
            .GetField("_interpreterUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(source)!;

        Assert.Equal(expected, url);
    }

    [Fact]
    public async Task TheRequestActuallyGoesToTheConfiguredPath()
    {
        // Caught only by watching the wire: the answer would still have parsed fine either way.
        var handler = new RecordingHandler("""{"elements":[]}""");
        var source = new OverpassVenueSource(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/") },
            Microsoft.Extensions.Options.Options.Create(new OverpassOptions
            {
                BaseUrl = "http://localhost/api",
            }),
            NullLogger<OverpassVenueSource>.Instance);

        await source.GetAroundAsync(new GeoPoint(25.2048, 55.2708));

        Assert.Equal("http://localhost/api/interpreter", handler.RequestedUrl);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _body;

        public RecordingHandler(string body) => _body = body;

        public string? RequestedUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestedUrl = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_body),
            });
        }
    }

    private sealed class NoopHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No request is expected when the URL is only being read");
    }
}
