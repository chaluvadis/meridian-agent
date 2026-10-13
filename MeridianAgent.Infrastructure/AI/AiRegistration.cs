namespace MeridianAgent.Infrastructure.AI;

using Microsoft.Extensions.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using OpenAI;

/// <summary>
/// One <see cref="IChatClient" /> for every agent, wired from the <c>AI</c> configuration section to
/// the Kilo AI Gateway, and one embedding generator when an embedding model is configured.
/// </summary>
/// <remarks>
/// Why the OpenAI SDK serves a Kilo app: the Kilo AI Gateway
/// (<see href="https://kilo.ai/docs/gateway"/>) is deliberately OpenAI-compatible — the same
/// <c>POST /chat/completions</c> dialect, the same Bearer-key header, the same <c>response_format</c>
/// switch for JSON mode — so the only difference from calling OpenAI directly is the base URL. That
/// keeps the JSON parsing, retry, and one-model-per-agent plumbing identical while the model behind
/// the key is swapped from a config line.
/// </remarks>
public static class AiRegistration
{
    /// <summary>
    /// Binds the <c>AI</c> section and registers the chat client (always) plus the embedding
    /// generator (only when an embedding model is configured). Fails loudly on a missing key: a
    /// pipeline with no usable model otherwise degrades into four agents that all answer with a
    /// fallback.
    /// </summary>
    public static IServiceCollection AddMeridianAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));

        var bound = new AiOptions();
        configuration.GetSection(AiOptions.SectionName).Bind(bound);

        if (string.IsNullOrWhiteSpace(bound.ApiKey))
        {
            throw new InvalidOperationException(
                "AI:ApiKey is missing. Set KILO_API_KEY (or AI__ApiKey) from " +
                "https://app.kilo.ai/profile — the app has no local model to fall back to.");
        }

        if (!Uri.TryCreate(bound.BaseUrl, UriKind.Absolute, out var baseUrl))
        {
            throw new InvalidOperationException(
                $"AI:BaseUrl '{bound.BaseUrl}' is not a valid URL. The default is {AiOptions.DefaultBaseUrl}.");
        }

        // The key rides in an Authorization header, so the endpoint has to be TLS — unless it is the
        // loopback address, where a plain host is how a local gateway is reached. A typo'd http://
        // base URL would otherwise send the key as cleartext on every call.
        if (!string.Equals(baseUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            var isLocalPlaintext = string.Equals(baseUrl.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                baseUrl.IsLoopback;

            if (!isLocalPlaintext)
            {
                throw new InvalidOperationException(
                    $"AI:BaseUrl must be https (a plain http:// endpoint would send the API key as " +
                    $"cleartext); got '{bound.BaseUrl}'.");
            }
        }

        var openAiClient = BuildClient(bound, baseUrl);

        // The chat client is registered through a factory so the wrapper takes the container's
        // logger: a backoff warning sent to a NullLogger would hide exactly the throttling evidence
        // a deploy needs when the API starts answering 429.
        services.AddSingleton<IChatClient>(serviceProvider => new RetryingChatClient(
            openAiClient.GetChatClient(bound.Model).AsIChatClient(),
            bound.RetryCount,
            serviceProvider.GetService<ILogger<RetryingChatClient>>()));

        // The Kilo Gateway hosts chat models only, so embeddings are opt-in: when an embedding model
        // is configured the rulebook is indexed and the safeguard searches it by similarity, and
        // when it is not the safeguard reads the full rulebook (see VectorRulebookNarrower).
        if (bound.EmbeddingsConfigured)
        {
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(serviceProvider =>
                new RetryingEmbeddingGenerator(
                    openAiClient.GetEmbeddingClient(bound.EmbeddingModel).AsIEmbeddingGenerator(),
                    bound.RetryCount,
                    serviceProvider.GetService<ILogger<RetryingEmbeddingGenerator>>()));
        }

        return services;
    }

    /// <summary>
    /// The gateway client every model call goes through. Two settings mark the boundary with local
    /// inference:
    /// <list type="bullet">
    /// <item><c>Endpoint</c> — the Kilo AI Gateway base URL. Every other OpenAI-compatible host works
    /// the same way by pointing this at it, which is what keeps the switch a one-line config
    /// change.</item>
    /// <item><c>RetryPolicy</c> — the SDK retries three times on its own, which stacked under
    /// <see cref="RetryingChatClient"/> would turn one call into nine requests with two compounding
    /// backoff ladders; the pass-through policy hands the whole retry budget to the wrapper.</item>
    /// </list>
    /// The network timeout keeps the old Ollama wiring's explicit clock rather than the SDK default.
    /// </summary>
    private static OpenAIClient BuildClient(AiOptions options, Uri baseUrl)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(baseUrl.AbsoluteUri.TrimEnd('/')),
            RetryPolicy = new SingleAttemptPolicy(),
            NetworkTimeout = TimeSpan.FromSeconds(Math.Max(1, options.RequestTimeoutSeconds)),
        };

        return new OpenAIClient(new System.ClientModel.ApiKeyCredential(options.ApiKey), clientOptions);
    }
}
