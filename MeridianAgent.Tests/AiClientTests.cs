namespace MeridianAgent.Tests;

using System.ClientModel;

using Microsoft.Extensions.AI;

using Microsoft.Extensions.Configuration;

using Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using MeridianAgent.Infrastructure.AI;

/// <summary>
/// The Kilo AI Gateway wiring that replaced the local Ollama host: the key guard, the gateway base
/// URL, the one-model pipeline decision, the opt-in embeddings, and the transport retries a hosted
/// API earns that a local one did not.
/// </summary>
public class AiClientTests
{
    private const string FakeKey = "kili-test-not-a-real-key";

    [Fact]
    public void AddMeridianAi_ThrowsWithoutAnApiKey()
    {
        // The pipeline with no usable model degrades into four agents that all answer with a
        // fallback, so a missing key is a startup failure rather than a quiet one.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddMeridianAi(configuration));

        Assert.Contains("KILO_API_KEY", exception.Message);
    }

    [Fact]
    public void AddMeridianAi_RejectsAPlaintextEndpoint()
    {
        // The key rides in an Authorization header, so an http:// endpoint would send it as cleartext
        // on every call — a typo'd base URL must fail at startup rather than leak quietly.
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddMeridianAi(Configuration(
                ("AI:ApiKey", FakeKey),
                ("AI:BaseUrl", "http://api.kilo.ai/api/gateway"))));

        Assert.Contains("https", exception.Message);
    }

    [Fact]
    public void AddMeridianAi_StillAcceptsALoopbackEndpointForALocalGateway()
    {
        // A self-hosted OpenAI-compatible gateway on the LAN is a real setup, and 127.0.0.1 never
        // leaves the machine, so the key is not exposed by it.
        var services = new ServiceCollection();
        services.AddMeridianAi(Configuration(
            ("AI:ApiKey", FakeKey),
            ("AI:BaseUrl", "http://127.0.0.1:11434/v1")));

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IChatClient>());
    }

    [Fact]
    public void AddMeridianAi_RejectsAMalformedBaseUrl()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddMeridianAi(Configuration(
                ("AI:ApiKey", FakeKey),
                ("AI:BaseUrl", "not-a-url"))));

        Assert.Contains("AI:BaseUrl", exception.Message);
    }

    [Fact]
    public void AddMeridianAi_RegistersOneChatClient()
    {
        var services = new ServiceCollection();
        services.AddMeridianAi(Configuration(("AI:ApiKey", FakeKey)));

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IChatClient>());
    }

    [Fact]
    public void AddMeridianAi_LeavesEmbeddingsOutUntilOneIsConfigured()
    {
        // The Kilo Gateway serves chat models only, so an unset embedding model must mean "no
        // embedding generator" rather than "register one that fails on first use": the narrower
        // then reads the full rulebook instead of indexing nothing.
        var services = new ServiceCollection();
        services.AddMeridianAi(Configuration(("AI:ApiKey", FakeKey)));

        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IEmbeddingGenerator<string, Embedding<float>>>());
    }

    [Fact]
    public void AddMeridianAi_RegistersAnEmbeddingGeneratorWhenOneIsConfigured()
    {
        // For anyone pointing AI:BaseUrl at a host that serves embeddings alongside chat.
        var services = new ServiceCollection();
        services.AddMeridianAi(Configuration(
            ("AI:ApiKey", FakeKey),
            ("AI:EmbeddingModel", "text-embedding-3-small")));

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IEmbeddingGenerator<string, Embedding<float>>>());
    }

    [Fact]
    public void AddMeridianAi_BindsEveryOptionFromConfiguration()
    {
        var services = new ServiceCollection();
        services.AddMeridianAi(Configuration(
            ("AI:ApiKey", FakeKey),
            ("AI:BaseUrl", "https://gateway.example.com/v1"),
            ("AI:Model", "anthropic/claude-haiku-4.5"),
            ("AI:EmbeddingModel", "text-embedding-3-large"),
            ("AI:RequestTimeoutSeconds", "60"),
            ("AI:RetryCount", "1")));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AiOptions>>().Value;

        Assert.Equal("https://gateway.example.com/v1", options.BaseUrl);
        Assert.Equal("anthropic/claude-haiku-4.5", options.Model);
        Assert.Equal("text-embedding-3-large", options.EmbeddingModel);
        Assert.Equal(60, options.RequestTimeoutSeconds);
        Assert.Equal(1, options.RetryCount);
        Assert.True(options.EmbeddingsConfigured);
    }

    [Fact]
    public void AddMeridianAi_DefaultsToTheKiloGatewayAndItsBalancedModel()
    {
        var services = new ServiceCollection();
        services.AddMeridianAi(Configuration(("AI:ApiKey", FakeKey)));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AiOptions>>().Value;

        Assert.Equal("https://api.kilo.ai/api/gateway", options.BaseUrl);
        Assert.Equal("anthropic/claude-sonnet-4.6", options.Model);
        Assert.Empty(options.EmbeddingModel);
        Assert.False(options.EmbeddingsConfigured);
        Assert.Equal(AiOptions.DefaultBaseUrl, options.BaseUrl);
        Assert.Equal(AiOptions.DefaultModel, options.Model);
        Assert.Equal(1536, AiOptions.DefaultVectorSize);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values)
    {
        var settings = values.ToDictionary(item => item.Key, item => (string?)item.Value);

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }
}

/// <summary>Transport retries: a hosted API says "slow down" far more often than a local one did.</summary>
public class RetryingChatClientTests
{
    [Fact]
    public async Task RetriesATransportFailureThenSucceeds()
    {
        var inner = new FlakyChatClient(failuresBeforeSuccess: 2);
        var client = new RetryingChatClient(inner, retryCount: 2);

        var response = await client.GetResponseAsync("hello");

        Assert.Equal("recovered", response.Text);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task GivesUpAfterTheConfiguredRetries()
    {
        var inner = new FlakyChatClient(failuresBeforeSuccess: 10);
        var client = new RetryingChatClient(inner, retryCount: 2);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetResponseAsync("hello"));

        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task DoesNotRetryAPermanentFailure()
    {
        // A rejected key or a bad request is a configuration problem, not the network's: answering
        // it three times slower would only hide the misconfiguration.
        var inner = new ThrowingChatClient(new InvalidOperationException("bad request"));
        var client = new RetryingChatClient(inner, retryCount: 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync("hello"));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task RetriesATimeout()
    {
        var inner = new FlakyChatClient(failuresBeforeSuccess: 1, exception: new TaskCanceledException("timed out"));
        var client = new RetryingChatClient(inner, retryCount: 1);

        var response = await client.GetResponseAsync("hello");

        Assert.Equal("recovered", response.Text);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task DoesNotRetryACancellation()
    {
        var token = new CancellationToken(canceled: true);
        var inner = new FlakyChatClient(failuresBeforeSuccess: 1);
        var client = new RetryingChatClient(inner, retryCount: 2);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.GetResponseAsync("hello", cancellationToken: token));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task RetryingEmbeddingGenerator_RetriesATransportFailureThenSucceeds()
    {
        // Indexing going down to one transient failure would quietly cost a destination its vector
        // search, and the narrower's catch-all would swallow the miss into a full-rulebook fallback.
        // Two failures then a success: the retry budget covers both, so indexing survives.
        var inner = new FlakyEmbeddings(failuresBeforeSuccess: 2);
        var generator = new RetryingEmbeddingGenerator(inner, retryCount: 2);

        var result = await generator.GenerateAsync(new[] { "Embedding" });

        Assert.Equal(3, inner.Calls);
        Assert.Single(result);
    }

    [Fact]
    public async Task RetryingEmbeddingGenerator_GivesUpAfterTheConfiguredRetries()
    {
        var inner = new FlakyEmbeddings(failuresBeforeSuccess: 10);
        var generator = new RetryingEmbeddingGenerator(inner, retryCount: 1);

        await Assert.ThrowsAsync<HttpRequestException>(() => generator.GenerateAsync(new[] { "Embedding" }));

        Assert.Equal(2, inner.Calls);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public void ModelRetryPolicy_TreatsThrottlingAndServerFlawsAsTransient(int status)
    {
        var exception = CreateException(status);

        Assert.True(ModelRetryPolicy.IsTransient(exception));
        Assert.Equal(TimeSpan.FromSeconds(2), ModelRetryPolicy.BackoffFor(1));
        Assert.Equal(TimeSpan.FromSeconds(30), ModelRetryPolicy.BackoffFor(10));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    public void ModelRetryPolicy_FailsFastOnAConfigurationProblem(int status)
    {
        var exception = CreateException(status);

        Assert.False(ModelRetryPolicy.IsTransient(exception));
    }

    [Fact]
    public void ModelRetryPolicy_TreatsARefusedConnectionAsTransientButNotABadRequest()
    {
        Assert.True(ModelRetryPolicy.IsTransient(new HttpRequestException("connection refused")));
        Assert.True(ModelRetryPolicy.IsTransient(new TaskCanceledException("timed out")));
        Assert.False(ModelRetryPolicy.IsTransient(new InvalidOperationException("bad request")));
    }

    private sealed class FlakyChatClient : IChatClient
    {
        private readonly int _failuresBeforeSuccess;
        private readonly Exception _exception;

        public FlakyChatClient(int failuresBeforeSuccess, Exception? exception = null)
        {
            _failuresBeforeSuccess = failuresBeforeSuccess;
            _exception = exception ?? new HttpRequestException("connection refused");
        }

        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;

            if (cancellationToken.IsCancellationRequested)
            {
                throw _exception;
            }

            return Calls <= _failuresBeforeSuccess
                ? throw _exception
                : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "recovered")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type key, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingChatClient(Exception exception) : IChatClient
    {
        private readonly Exception _exception = exception;

        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            throw _exception;
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type key, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// The health check that turns a bad key into a failed deploy instead of a green container that
    /// answers every query with a fallback.
    /// </summary>
    public class GatewayHealthCheckTests
    {
        [Fact]
        public async Task ARejectedKeyMakesTheContainerUnhealthy()
        {
            var check = new GatewayHealthCheck(
                new StatusChatClient(401),
                NullLogger<GatewayHealthCheck>.Instance);

            var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

            Assert.Equal(HealthStatus.Unhealthy, result.Status);
        }

        [Fact]
        public async Task AnEmptyBalanceIsAlsoUnhealthy()
        {
            var check = new GatewayHealthCheck(
                new StatusChatClient(402),
                NullLogger<GatewayHealthCheck>.Instance);

            var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

            Assert.Equal(HealthStatus.Unhealthy, result.Status);
        }

        [Fact]
        public async Task AServerErrorIsDegradedNotUnhealthy()
        {
            // A 500 or an unreachable gateway says nothing about the key, and the next query may
            // well succeed — failing the deploy over it would chase the wrong problem.
            var check = new GatewayHealthCheck(
                new StatusChatClient(500),
                NullLogger<GatewayHealthCheck>.Instance);

            var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

            Assert.Equal(HealthStatus.Degraded, result.Status);
        }

        [Fact]
        public async Task AWorkingKeyIsHealthyAndProbesAtMostOncePerInterval()
        {
            var inner = new StatusChatClient(200);
            var check = new GatewayHealthCheck(inner, NullLogger<GatewayHealthCheck>.Instance);

            var first = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
            var second = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

            Assert.Equal(HealthStatus.Healthy, first.Status);
            Assert.Equal(HealthStatus.Healthy, second.Status);
            Assert.Single(inner.Prompts);
        }

        private sealed class StatusChatClient(int status) : IChatClient
        {
            private readonly int _status = status;

            public List<string> Prompts { get; } = new();

            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                Prompts.Add(messages.LastOrDefault(message => message.Role == ChatRole.User)?.Text ?? string.Empty);

                return _status == 200
                    ? Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")))
                    : throw CreateException(_status);
            }

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.CompletedTask;
                yield break;
            }

            public object? GetService(Type key, object? serviceKey = null) => null;

            public void Dispose()
            {
            }
        }
    }

    private static System.ClientModel.ClientResultException CreateException(int status) =>
        new(new StubResponse(status), null);

    private sealed class StubResponse(int status) : System.ClientModel.Primitives.PipelineResponse
    {
        public override int Status => status;

        public override string ReasonPhrase => $"HTTP {status}";

        protected override System.ClientModel.Primitives.PipelineResponseHeaders HeadersCore => null!;

        public override System.IO.Stream? ContentStream { get; set; }

        public override BinaryData Content => BinaryData.Empty;

        public override BinaryData BufferContent(System.Threading.CancellationToken cancellationToken) => BinaryData.Empty;

        public override ValueTask<BinaryData> BufferContentAsync(System.Threading.CancellationToken cancellationToken) =>
            new(BinaryData.Empty);

        public override void Dispose()
        {
        }
    }

    private sealed class FlakyEmbeddings(int failuresBeforeSuccess) : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly int _failuresBeforeSuccess = failuresBeforeSuccess;

        public int Calls { get; private set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;

            return Calls <= _failuresBeforeSuccess
                ? throw new HttpRequestException("connection refused")
                : Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                    values.Select(_ => new Embedding<float>(new float[1536]))));
        }

        public object? GetService(Type key, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
