namespace MeridianAgent.Infrastructure.AI;

/// <summary>A single call to a locally hosted Ollama model.</summary>
/// <param name="Model">Ollama model tag, e.g. "qwen2.5:1.5b".</param>
/// <param name="Prompt">The rendered prompt.</param>
/// <param name="JsonMode">When true, Ollama is forced to emit JSON ("format": "json").</param>
public sealed record LlmRequest(
    string Model,
    string Prompt,
    bool JsonMode = true,
    double Temperature = 0.2,
    int? MaxTokens = null);

public interface ILlmClient
{
    Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamAsync(LlmRequest request, CancellationToken cancellationToken = default);

    Task<float[]> EmbedAsync(LlmRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Raised when the local Ollama host is unreachable or returns an error.</summary>
public sealed class LlmException(string message, Exception? innerException = null) : Exception(message, innerException)
{
    /// <summary>
    /// The HTTP status Ollama answered with, when the failure arrived as a response rather than a
    /// transport error. Server-side statuses are the ones worth another attempt.
    /// </summary>
    public int? StatusCode { get; init; }
}
