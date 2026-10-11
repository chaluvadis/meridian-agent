namespace MeridianAgent.Tests;

using System.Net;

using MeridianAgent.Infrastructure.AI;

public class OllamaClientTests
{
    private static OllamaClient CreateClient(RecordingHandler handler, OllamaOptions? options = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") },
            Microsoft.Extensions.Options.Options.Create(options ?? new OllamaOptions { RetryCount = 2 }),
            NullLogger<OllamaClient>.Instance);

    private static LlmRequest Request(
        string model = "qwen2.5:1.5b",
        string prompt = "Anywhere for shawarma?",
        bool jsonMode = true,
        int? maxTokens = null) =>
        new(model, prompt, jsonMode, 0.2, maxTokens);

    private static HttpResponseMessage OkJson(object payload) => new()
    {
        StatusCode = HttpStatusCode.OK,
        Content = JsonContent.Create(payload),
    };

    [Fact]
    public async Task CompleteAsync_PostsThePromptToApiChat()
    {
        var handler = new RecordingHandler(OkJson(new { message = new { content = "Karak House is open." } }));
        var client = CreateClient(handler);

        var reply = await client.CompleteAsync(Request(prompt: "Anywhere for shawarma?", jsonMode: false));

        Assert.Equal("Karak House is open.", reply);
        Assert.Equal("POST", handler.Method);
        Assert.Equal("/api/chat", handler.Path);

        var body = handler.Body!;
        Assert.Contains("\"content\":\"Anywhere for shawarma?\"", body);
        Assert.Contains("\"stream\":false", body);
        Assert.DoesNotContain("\"format\":\"json\"", body);
    }

    [Fact]
    public async Task CompleteAsync_ForcesJsonWhenJsonModeIsOn()
    {
        var handler = new RecordingHandler(OkJson(new { message = new { content = "{}" } }));
        var client = CreateClient(handler);

        await client.CompleteAsync(Request(jsonMode: true));

        Assert.Contains("\"format\":\"json\"", handler.Body!);
    }

    [Fact]
    public async Task CompleteAsync_UsesTheConfiguredTokenLimitWhenTheRequestHasNone()
    {
        var handler = new RecordingHandler(OkJson(new { message = new { content = "ok" } }));
        var client = CreateClient(handler, new OllamaOptions { MaxTokens = 777, RetryCount = 0 });

        await client.CompleteAsync(Request());

        Assert.Contains("\"num_predict\":777", handler.Body!);
    }

    [Fact]
    public async Task CompleteAsync_HandsBackAFencedReplyForTheExtractorToStrip()
    {
        const string Fenced = "```json\n{\"top_pick\": \"Karak House\"}\n```";
        var handler = new RecordingHandler(OkJson(new { message = new { content = Fenced } }));
        var client = CreateClient(handler);

        // Ollama returns the fences untouched; the extractor is what repairs them.
        var reply = await client.CompleteAsync(Request());

        Assert.Equal(Fenced, reply);
        Assert.True(JsonExtractor.TryExtract(reply, out var document));
        Assert.Equal("Karak House", document!.RootElement.GetProperty("top_pick").GetString());
    }

    [Fact]
    public async Task CompleteAsync_RejectsAnEmptyReply()
    {
        var handler = new RecordingHandler(OkJson(new { message = new { content = "   " } }));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync(Request()));
    }

    [Fact]
    public async Task CompleteAsync_ReportsAnHttpFailureWithItsStatusAndBody()
    {
        var handler = new RecordingHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.InternalServerError,
            Content = new StringContent("model not found"),
        });
        var client = CreateClient(handler, new OllamaOptions { RetryCount = 0 });

        var error = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync(Request()));

        Assert.Contains("500", error.Message);
        Assert.Contains("model not found", error.Message);
        Assert.Equal(500, error.StatusCode);
    }

    [Fact]
    public async Task CompleteAsync_DoesNotRetryAClientError()
    {
        var handler = new RecordingHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.BadRequest,
            Content = new StringContent("bad model"),
        });
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync(Request()));

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CompleteAsync_RetriesAServerErrorUntilTheAttemptsRunOut()
    {
        var handler = new RecordingHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.InternalServerError,
            Content = new StringContent("boom"),
        });
        var client = CreateClient(handler);

        var error = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync(Request()));

        Assert.Contains("after 3 attempts", error.Message);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task CompleteAsync_RetriesATransportFailure()
    {
        var handler = new RecordingHandler(OkJson("never"));
        handler.FailAttempts(10, new HttpRequestException("connection refused"));
        var client = CreateClient(handler);

        var error = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync(Request()));

        Assert.Contains("connection refused", error.Message);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task CompleteAsync_SucceedsOnTheSecondAttemptAfterATransportFailure()
    {
        var handler = new RecordingHandler(OkJson(new { message = new { content = "recovered" } }));
        handler.FailAttempts(1, new HttpRequestException("connection refused"));
        var client = CreateClient(handler);

        Assert.Equal("recovered", await client.CompleteAsync(Request()));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task StreamAsync_YieldsEveryChunkAndSkipsTheUnreadableOnes()
    {
        var body = string.Join(
            '\n',
            "{\"message\":{\"content\":\"Top \"}}",
            "",
            "not json at all",
            "{\"message\":{\"content\":\"pick: \"}}",
            "{\"message\":{\"content\":\"Karak House.\"}}");
        var handler = new RecordingHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(body),
        });
        var client = CreateClient(handler);

        var chunks = new List<string>();
        await foreach (var chunk in client.StreamAsync(Request(jsonMode: false)))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(new[] { "Top ", "pick: ", "Karak House." }, chunks);
        Assert.Equal("/api/chat", handler.Path);
        Assert.Contains("\"stream\":true", handler.Body!);
    }

    [Fact]
    public async Task EmbedAsync_PostsToApiEmbeddingsAndReturnsTheVector()
    {
        var handler = new RecordingHandler(OkJson(new { embedding = new[] { 0.5f, 0.25f } }));
        var client = CreateClient(handler);

        var vector = await client.EmbedAsync(Request());

        Assert.Equal(new[] { 0.5f, 0.25f }, vector);
        Assert.Equal("/api/embeddings", handler.Path);
    }

    [Fact]
    public async Task EmbedAsync_RejectsAnEmptyVector()
    {
        var handler = new RecordingHandler(OkJson(new { embedding = Array.Empty<float>() }));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<LlmException>(() => client.EmbedAsync(Request()));
    }

    /// <summary>
    /// Stands in for the wire: records what each attempt looked like, and answers with a canned
    /// response or a canned failure for the first N attempts. The request body is captured as a
    /// string because the client disposes its request as soon as it has sent it.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        private readonly List<HttpRequestException> _planned = new();

        private int _attempts;
        private int _plannedUsed;

        public RecordingHandler(HttpResponseMessage response) => _response = response;

        public string? Method { get; private set; }

        public string? Path { get; private set; }

        public string? Body { get; private set; }

        public int Calls => _attempts;

        /// <summary>Throws for the first <paramref name="count"/> attempts, then answers normally.</summary>
        public void FailAttempts(int count, HttpRequestException failure)
        {
            for (var attempt = 0; attempt < count; attempt++)
            {
                _planned.Add(failure);
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _attempts++;

            Method = request.Method.Method;
            Path = request.RequestUri?.AbsolutePath;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (_plannedUsed < _planned.Count)
            {
                throw _planned[_plannedUsed++];
            }

            // A fresh copy per attempt: a response body can only be read once.
            var responseBody = await _response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(_response.StatusCode) { Content = new StringContent(responseBody) };
        }
    }
}
