namespace MeridianAgent.Infrastructure.AI;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    /// <summary>Base URL of the local Ollama host (env: Ollama__BaseUrl).</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Seconds before an Ollama call is abandoned (env: Ollama__RequestTimeoutSeconds).</summary>
    public int RequestTimeoutSeconds { get; set; } = 300;

    /// <summary>Retries for transient HTTP failures (env: Ollama__RetryCount).</summary>
    public int RetryCount { get; set; } = 2;

    /// <summary>Sampling temperature for replies (env: Ollama__Temperature).</summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>Reply length a call gets when it does not ask for its own (env: Ollama__MaxTokens).</summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>Model used to embed text for vector search (env: Ollama__EmbeddingModel).</summary>
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Model used by the JSON sanitizer fallback (env: Ollama__SanitizerModel).</summary>
    public string SanitizerModel { get; set; } = "qwen2.5:1.5b";
}
