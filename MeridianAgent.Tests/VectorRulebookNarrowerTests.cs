namespace MeridianAgent.Tests;

using System.Net;

using MeridianAgent.Infrastructure.Data;

using Microsoft.Extensions.AI;

using Microsoft.Extensions.Options;

/// <summary>
/// The indexing path that turned the OpenAI switch from "the app works" into "the app works *and*
/// stays indexed": everything is embedded before the collection is touched, so a rejected key or a
/// dead quote leaves the existing vectors exactly where they were.
/// </summary>
public class VectorRulebookNarrowerTests
{
    private static DestinationRulebook Book => new()
    {
        Destination = "Dubai, UAE",
        Rules = new[]
        {
            new MeridianAgent.Domain.Entities.SafeguardRule
            {
                RuleId = "dubai-photo-1",
                Category = MeridianAgent.Domain.RuleCategory.Photography,
                Rule = "Photographing people without permission is an offence.",
                Severity = MeridianAgent.Domain.RiskSeverity.Medium,
            },
            new MeridianAgent.Domain.Entities.SafeguardRule
            {
                RuleId = "dubai-public-2",
                Category = MeridianAgent.Domain.RuleCategory.Pda,
                Rule = "Public displays of affection are illegal.",
                Severity = MeridianAgent.Domain.RiskSeverity.Low,
            },
        },
    };

    [Fact]
    public async Task EnsureIndexedAsync_LeavesTheCollectionAloneWhenEmbeddingFails()
    {
        // The delete-then-rebuild order loses the old vectors before their replacements exist: a
        // rejected key on the first embed used to drop the collection and then fail, leaving vector
        // search silently dead until a restart happened to succeed.
        var qdrant = new CountingQdrantClient();
        var narrower = new VectorRulebookNarrower(
            new ThrowingEmbeddings(),
            qdrant.Client,
            NullLogger<VectorRulebookNarrower>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() => narrower.EnsureIndexedAsync(Book));

        Assert.Equal(0, qdrant.Requests);
    }

    [Fact]
    public async Task EnsureIndexedAsync_TouchesTheCollectionOnlyAfterEmbeddingSucceeds()
    {
        var qdrant = new CountingQdrantClient();
        var narrower = new VectorRulebookNarrower(
            new WorkingEmbeddings(),
            qdrant.Client,
            NullLogger<VectorRulebookNarrower>.Instance);

        await narrower.EnsureIndexedAsync(Book);

        // The size-check GET, then the create, then one upsert per rule — and only after both
        // embeds have landed.
        Assert.Equal(4, qdrant.Requests);
        Assert.EndsWith("collections/meridian-rules-dubai%2C%20uae", qdrant.Paths![0]);
        Assert.Equal(2, qdrant.Paths.Count(path => path.EndsWith("/points", StringComparison.Ordinal)));
    }

    private sealed class ThrowingEmbeddings : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("OpenAI rejected the API key");

        public object? GetService(Type key, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class WorkingEmbeddings : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                values.Select(_ => new Embedding<float>(new float[MeridianAgent.Infrastructure.AI.AiOptions.DefaultVectorSize]))));

        public object? GetService(Type key, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>A Qdrant client backed by a counting handler: nothing is actually stored.</summary>
    private sealed class CountingQdrantClient
    {
        public CountingQdrantClient() =>
            Client = new QdrantClient(
                new HttpClient(new RecordingHandler(this)) { BaseAddress = new Uri("http://localhost:6333") },
                Options.Create(new QdrantOptions()),
                NullLogger<QdrantClient>.Instance);

        public QdrantClient Client { get; }

        public List<string>? Paths { get; } = new();

        public int Requests => Paths!.Count;

        public void Record(string path) => Paths!.Add(path);

        private sealed class RecordingHandler(CountingQdrantClient owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                owner.Record(request.RequestUri!.AbsolutePath);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"result": {"config": {"params": {"vectors": {"size": 1536}}}}}"""),
                });
            }
        }
    }
}

/// <summary>The knobs an operator may turn must actually reach the model call.</summary>
public class ModelOptionWiringTests
{
    [Fact]
    public async Task TheAgentsSendTheConfiguredTemperature()
    {
        // A documented knob that changes nothing is worse than a knob that does not exist.
        var client = new ScriptedLlmClient(
            """[{"venue_name": "Karak House", "venue_type": "indoor", "category": "cafe"}]""");
        var agent = new SearchAgent(
            client,
            NullLogger<SearchAgent>.Instance,
            Options.Create(new MeridianAgent.Infrastructure.AI.AiOptions { Temperature = 0.9 }));

        await agent.ProposeAsync(TestData.Constraints, TestData.Request());

        Assert.Equal(0.9, client.Calls[0].Temperature!.Value, 3);
    }

    [Fact]
    public async Task TheSanitizerCallGetsTheConfiguredReplyCap()
    {
        // A repair with no token cap runs unbilled, so the repair inherits the agent's cap.
        var client = new ScriptedLlmClient(
            "not json at all",
            """{"status": "CLEARED", "warnings": []}""");
        var agent = new SafeguardAgent(
            client,
            NullLogger<SafeguardAgent>.Instance,
            Options.Create(new MeridianAgent.Infrastructure.AI.AiOptions { MaxTokens = 318 }));

        await agent.CheckAsync(new ProposedItinerary(Array.Empty<Venue>()), TestData.Request());

        Assert.Equal(318, client.Calls[0].MaxTokens);
        Assert.Equal(318, client.Calls[1].MaxTokens);
    }
}
