namespace MeridianAgent.Tests;

using System.Net;

using MeridianAgent.Infrastructure.External;

public class TravelContextClientTests : IDisposable
{
    private readonly StubHandler _handler = new();

    public void Dispose() => _handler.Dispose();

    private NominatimClient CreateNominatim() =>
        new(new HttpClient(_handler) { BaseAddress = new Uri("https://nominatim.openstreetmap.org") },
            Microsoft.Extensions.Options.Options.Create(new NominatimOptions()));

    [Fact]
    public async Task GeocodeAsync_ReturnsTheFirstResultAsAPoint()
    {
        _handler.Respond("""
            [{"lat":"25.2048","lon":"55.2708","display_name":"Dubai, United Arab Emirates"}]
            """);
        var client = CreateNominatim();

        var point = await client.GeocodeAsync("Dubai");

        Assert.NotNull(point);
        Assert.Equal(25.2048, point!.Latitude);
        Assert.Equal(55.2708, point.Longitude);
    }

    [Fact]
    public async Task GeocodeAsync_EscapesTheQueryAndAsksForJsonV2()
    {
        _handler.Respond("[]");
        var client = CreateNominatim();

        await client.GeocodeAsync("Al Fahidi & Bur Dubai");

        Assert.Equal("/search", _handler.Path);
        Assert.Contains("q=Al%20Fahidi%20%26%20Bur%20Dubai", _handler.Query);
        Assert.Contains("format=jsonv2", _handler.Query);
        Assert.Contains("limit=1", _handler.Query);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GeocodeAsync_DoesNotAskForABlankPlace(string query)
    {
        var client = CreateNominatim();

        Assert.Null(await client.GeocodeAsync(query));
        Assert.Equal(0, _handler.Calls);
    }

    [Fact]
    public async Task GeocodeAsync_ReturnsNullWhenTheServiceFails()
    {
        _handler.Status = HttpStatusCode.ServiceUnavailable;
        var client = CreateNominatim();

        Assert.Null(await client.GeocodeAsync("Dubai"));
    }

    [Fact]
    public async Task GeocodeAsync_ReturnsNullWhenTheAnswerHasNoUsablePosition()
    {
        _handler.Respond("""[{"lat":"","lon":"","display_name":"nowhere"}]""");
        var client = CreateNominatim();

        Assert.Null(await client.GeocodeAsync("Dubai"));

        _handler.Respond("""[{"lat":"oops","lon":"55.2708"}]""");
        Assert.Null(await client.GeocodeAsync("Dubai"));
    }

    [Fact]
    public async Task WeatherClient_ReturnsNothingUntilAKeyIsConfigured()
    {
        var weather = new WeatherClient(
            new HttpClient(new StubHandler()) { BaseAddress = new Uri("https://api.openweathermap.org") },
            Microsoft.Extensions.Options.Options.Create(new WeatherOptions { ApiKey = string.Empty }));

        Assert.False(weather.IsConfigured);
        Assert.Null(await weather.GetCurrentAsync(new GeoPoint(25.2, 55.3)));
    }

    [Fact]
    public async Task WeatherClient_ReadsTemperatureConditionAndRain()
    {
        _handler.Respond("""
            {"main":{"temp":31.4},"weather":[{"main":"Rain","description":"light rain"}],"rain":{"1h":0.4},"timezone":14400}
            """);
        var weather = new WeatherClient(
            new HttpClient(_handler) { BaseAddress = new Uri("https://api.openweathermap.org") },
            Microsoft.Extensions.Options.Options.Create(new WeatherOptions { ApiKey = "test-key" }));

        var reading = await weather.GetCurrentAsync(new GeoPoint(25.2048, 55.2708));

        Assert.NotNull(reading);
        Assert.Equal(31.4, reading!.TemperatureC);
        Assert.Equal("Rain", reading.Condition);
        Assert.Equal("light rain", reading.Description);
        Assert.True(reading.IsRaining);
        Assert.Equal(14400, reading.TimezoneOffsetSeconds);

        Assert.Contains("lat=25.2048", _handler.Query);
        Assert.Contains("lon=55.2708", _handler.Query);
        Assert.Contains("units=metric", _handler.Query);
        Assert.Contains("appid=test-key", _handler.Query);
    }

    [Fact]
    public async Task WeatherClient_ReturnsNullWhenThePayloadIsIncomplete()
    {
        _handler.Respond("""{"weather":[{"main":"Clear"}]}""");
        var weather = new WeatherClient(
            new HttpClient(_handler) { BaseAddress = new Uri("https://api.openweathermap.org") },
            Microsoft.Extensions.Options.Options.Create(new WeatherOptions { ApiKey = "test-key" }));

        Assert.Null(await weather.GetCurrentAsync(new GeoPoint(25.2, 55.3)));
    }

    [Fact]
    public async Task WeatherClient_ReturnsNullWhenTheServiceFails()
    {
        _handler.Status = HttpStatusCode.Unauthorized;
        var weather = new WeatherClient(
            new HttpClient(_handler) { BaseAddress = new Uri("https://api.openweathermap.org") },
            Microsoft.Extensions.Options.Options.Create(new WeatherOptions { ApiKey = "bad-key" }));

        Assert.Null(await weather.GetCurrentAsync(new GeoPoint(25.2, 55.3)));
    }

    /// <summary>
    /// Answers every request with one canned body until told otherwise, and records what it was
    /// asked for. The content is copied per attempt so a second read is not a disposed one.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private string _body = "[]";
        private HttpStatusCode _status = HttpStatusCode.OK;

        public string? Path { get; private set; }

        public string? Query { get; private set; }

        public int Calls { get; private set; }

        public HttpStatusCode Status
        {
            get => _status;
            set => _status = value;
        }

        public void Respond(string body) => _body = body;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;

            var uri = request.RequestUri!;
            Path = uri.AbsolutePath;
            Query = uri.Query;

            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json"),
            };

            return await Task.FromResult(response).ConfigureAwait(false);
        }
    }
}
