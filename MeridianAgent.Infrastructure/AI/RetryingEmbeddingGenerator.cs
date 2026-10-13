namespace MeridianAgent.Infrastructure.AI;

using Microsoft.Extensions.AI;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Embeddings behind the Qdrant rulebook search, retried through the same transient failures as the
/// chat client — <see cref="ModelRetryPolicy"/> owns the classification and the backoff for both, so
/// the two cannot drift apart. A restart of indexing is cheap, so a hiccup mid-index should not cost
/// the whole destination its vector search.
/// </summary>
public sealed class RetryingEmbeddingGenerator : DelegatingEmbeddingGenerator<string, Embedding<float>>
{
    private readonly int _retryCount;
    private readonly ILogger _logger;

    public RetryingEmbeddingGenerator(
        IEmbeddingGenerator<string, Embedding<float>> innerGenerator,
        int retryCount,
        ILogger? logger = null)
        : base(innerGenerator)
    {
        _retryCount = Math.Max(0, retryCount);
        _logger = logger ?? NullLogger.Instance;
    }

    public override async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await InnerGenerator.GenerateAsync(values, options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ModelRetryPolicy.IsTransient(ex) && attempt <= _retryCount && !cancellationToken.IsCancellationRequested)
            {
                var delay = ModelRetryPolicy.BackoffFor(attempt);
                _logger.LogWarning(
                    "Model embedding call failed on attempt {Attempt}/{Attempts} ({Delay}s backoff): {Message}",
                    attempt,
                    _retryCount + 1,
                    delay.TotalSeconds,
                    ex.Message);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
