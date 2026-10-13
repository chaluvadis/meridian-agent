namespace MeridianAgent.Infrastructure.AI;

/// <summary>
/// Everything the hosted model needs to be told. The models come from the Kilo AI Gateway —
/// <see href="https://kilo.ai/docs/gateway"/> — a single OpenAI-compatible endpoint that serves models
/// from Anthropic, Google, xAI, DeepSeek and more under one API key. Because the gateway speaks the
/// chat-completions dialect, the same SDK wiring works against any compatible host by changing
/// <see cref="BaseUrl"/>. Environment overrides use the <c>AI__</c> prefix, e.g. <c>AI__ApiKey</c>,
/// <c>AI__Model=anthropic/claude-sonnet-4.6</c>.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "AI";

    /// <summary>Gateway the models are served from (env: AI__BaseUrl).</summary>
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>API key for the gateway (env: AI__ApiKey, exported as KILO_API_KEY by compose).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Chat model every agent and the JSON repair fallback run on (env: AI__Model).</summary>
    public string Model { get; set; } = DefaultModel;

    /// <summary>
    /// Embedding model for the Qdrant rulebook search (env: AI__EmbeddingModel), or empty for none.
    /// The Kilo Gateway hosts chat models only, so the default is off: with no embeddings the
    /// safeguard reads the full rulebook, which is a handful of rules per destination.
    /// </summary>
    public string EmbeddingModel { get; set; } = string.Empty;

    /// <summary>Sampling temperature when an agent does not choose its own (env: AI__Temperature).</summary>
    public double Temperature { get; set; } = DefaultTemperature;

    /// <summary>Reply length a call gets when it does not ask for its own (env: AI__MaxTokens).</summary>
    public int MaxTokens { get; set; } = DefaultMaxTokens;

    /// <summary>Seconds before a model call is abandoned (env: AI__RequestTimeoutSeconds).</summary>
    public int RequestTimeoutSeconds { get; set; } = 120;

    /// <summary>Retries for transient model failures (env: AI__RetryCount).</summary>
    public int RetryCount { get; set; } = 2;

    /// <summary>Whether an embedding model is configured, so the rulebook can be indexed.</summary>
    public bool EmbeddingsConfigured => !string.IsNullOrWhiteSpace(EmbeddingModel);

    /// <summary>The Kilo AI Gateway base URL every default request goes to.</summary>
    public const string DefaultBaseUrl = "https://api.kilo.ai/api/gateway";

    /// <summary>The chat model this app is built around.</summary>
    public const string DefaultModel = "anthropic/claude-sonnet-4.6";

    /// <summary>
    /// Vector length of the embedding model an operator configures — 1536 for text-embedding-3-small,
    /// 3072 for text-embedding-3-large. No default model is named because the gateway hosts none.
    /// </summary>
    public const int DefaultVectorSize = 1536;

    /// <summary>
    /// The reply cap every model call shares. One number, referenced by <see cref="Agents.AgentBase"/>
    /// and the JSON repair path, so the agent calls and the repair call cannot drift apart.
    /// </summary>
    public const int DefaultMaxTokens = 2048;

    /// <summary>The sampling temperature a JSON call uses when nothing better is configured.</summary>
    public const double DefaultTemperature = 0.2;
}
