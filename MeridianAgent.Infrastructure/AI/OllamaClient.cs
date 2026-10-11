namespace MeridianAgent.Infrastructure.AI;

/// <summary>
/// HTTP client for the local Ollama host: /api/chat (blocking and streaming) and /api/embeddings.
/// Uses Polly retries with exponential backoff and logs every call with model, latency, and token counts.
/// </summary>
public sealed class OllamaClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaClient> _logger;
    private readonly IAsyncPolicy _retryPolicy;

    public OllamaClient(HttpClient httpClient, IOptions<OllamaOptions> options, ILogger<OllamaClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        var baseUrl = _options.BaseUrl.TrimEnd('/');
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(baseUrl);
        }

        _retryPolicy = Policy
            .Handle<LlmException>(IsTransient)
            .WaitAndRetryAsync(
                _options.RetryCount,
                attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                (exception, delay, attempt, _) =>
                {
                    _logger.LogWarning(
                        "Ollama call failed on attempt {Attempt}/{MaxAttempts} ({Delay}s backoff): {Message}",
                        attempt,
                        _options.RetryCount + 1,
                        delay.TotalSeconds,
                        exception.Message);
                });
    }

    /// <summary>
    /// A failure worth another attempt: transport trouble (a refused connection, a timeout) or a
    /// server-side status. A 4xx is the caller's error and is reported immediately. The transport
    /// exceptions are already wrapped by the time they reach here, so the check looks at the
    /// wrapper rather than the raw type.
    /// </summary>
    private static bool IsTransient(LlmException exception) =>
        exception.InnerException is HttpRequestException or TaskCanceledException ||
        exception.StatusCode is >= 500;

    /// <summary>Asks Ollama for a complete reply and returns its text.</summary>
    public async Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var payload = new ChatRequest(
            request.Model,
            [new ChatMessage("user", request.Prompt)],
            Stream: false,
            Format: request.JsonMode ? "json" : null,
            new ChatOptions(request.Temperature, request.MaxTokens ?? _options.MaxTokens));

        var response = await ExecuteWithRetryAsync(
            () => SendAsync(payload, HttpCompletionOption.ResponseContentRead, cancellationToken),
            request.Model,
            cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadFromJsonAsync<ChatChunk>(cancellationToken).ConfigureAwait(false);
        var content = body?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new LlmException($"Ollama returned an empty response for model '{request.Model}'.");
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Ollama complete: model={Model}, json_mode={JsonMode}, prompt_chars={PromptChars}, prompt_tokens={PromptTokens}, completion_tokens={CompletionTokens}, {ElapsedMs}ms",
            request.Model,
            request.JsonMode,
            request.Prompt.Length,
            body?.PromptEvalCount ?? 0,
            body?.EvalCount ?? 0,
            stopwatch.ElapsedMilliseconds);

        return content;
    }

    /// <summary>Streams a reply token by token; unreadable chunks are skipped rather than failing the run.</summary>
    public async IAsyncEnumerable<string> StreamAsync(
        LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var payload = new ChatRequest(
            request.Model,
            [new ChatMessage("user", request.Prompt)],
            Stream: true,
            Format: request.JsonMode ? "json" : null,
            new ChatOptions(request.Temperature, request.MaxTokens ?? _options.MaxTokens));

        var response = await ExecuteWithRetryAsync(
            () => SendAsync(payload, HttpCompletionOption.ResponseHeadersRead, cancellationToken),
            request.Model,
            cancellationToken).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var tokens = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            ChatChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<ChatChunk>(line, JsonDefaults.Options);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping unreadable Ollama stream chunk");
                continue;
            }

            var delta = chunk?.Message?.Content;
            if (!string.IsNullOrEmpty(delta))
            {
                tokens++;
                yield return delta;
            }
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Ollama stream: model={Model}, chunks={Chunks}, {ElapsedMs}ms",
            request.Model,
            tokens,
            stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Embeds one prompt into a vector, used by the Qdrant rulebook search.</summary>
    public async Task<float[]> EmbedAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new EmbeddingRequest(request.Model, request.Prompt);

        var response = await ExecuteWithRetryAsync(
            () => SendEmbeddingsAsync(payload, cancellationToken),
            request.Model,
            cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken).ConfigureAwait(false);
        var embedding = body?.Embedding;
        if (embedding is null || embedding.Length == 0)
        {
            throw new LlmException($"Ollama returned no embedding for model '{request.Model}'.");
        }

        return embedding;
    }

    private async Task<HttpResponseMessage> SendAsync(
        ChatRequest payload,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = JsonContent.Create(payload),
        };

        try
        {
            return await _httpClient
                .SendAsync(httpRequest, completionOption, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException(
                $"Cannot reach Ollama at {_options.BaseUrl} ({ex.Message}). Start it with `ollama serve` or set Ollama__BaseUrl.",
                ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LlmException($"Ollama request timed out after {_options.RequestTimeoutSeconds}s.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendEmbeddingsAsync(
        EmbeddingRequest payload,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/embeddings")
        {
            Content = JsonContent.Create(payload),
        };

        try
        {
            return await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException(
                $"Cannot reach Ollama at {_options.BaseUrl} ({ex.Message}). Start it with `ollama serve` or set Ollama__BaseUrl.",
                ex);
        }
    }

    private async Task<HttpResponseMessage> ExecuteWithRetryAsync(
        Func<Task<HttpResponseMessage>> send,
        string model,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _retryPolicy
                .ExecuteAsync(async ct => await SendOrThrowAsync(send, model, ct), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (LlmException ex) when (IsTransient(ex))
        {
            throw new LlmException(
                $"Ollama call for model '{model}' failed after {_options.RetryCount + 1} attempts: {ex.Message}",
                ex)
            {
                StatusCode = ex.StatusCode,
            };
        }
    }

    private static async Task<HttpResponseMessage> SendOrThrowAsync(
        Func<Task<HttpResponseMessage>> send,
        string model,
        CancellationToken cancellationToken)
    {
        var response = await send().ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var body = await SafeReadAsync(response).ConfigureAwait(false);
        var status = response.StatusCode;
        response.Dispose();
        throw new LlmException($"Ollama returned HTTP {(int)status} for model '{model}': {body}")
        {
            StatusCode = (int)status,
        };
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed record ChatRequest(
        string Model,
        ChatMessage[] Messages,
        bool Stream,
        string? Format,
        ChatOptions Options);

    private sealed record ChatMessage(string Role, string Content);

    // Snaking Ollama's option names is not cosmetic: JsonContent.Create writes camelCase, and the
    // Go decoder Ollama runs does not treat "numPredict" and "num_predict" as the same field, so
    // without this the reply length limit would be quietly dropped.
    private sealed record ChatOptions(
        double Temperature,
        [property: JsonPropertyName("num_predict")] int NumPredict);

    private sealed record ChatChunk
    {
        [JsonPropertyName("message")]
        public ChatMessageContent? Message { get; init; }

        [JsonPropertyName("prompt_eval_count")]
        public int? PromptEvalCount { get; init; }

        [JsonPropertyName("eval_count")]
        public int? EvalCount { get; init; }
    }

    private sealed record ChatMessageContent
    {
        [JsonPropertyName("content")]
        public string? Content { get; init; }
    }

    private sealed record EmbeddingRequest(string Model, string Prompt);

    private sealed record EmbeddingResponse
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; init; }
    }
}
