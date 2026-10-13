namespace MeridianAgent.Infrastructure.AI;

using Microsoft.Extensions.AI;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// A chat client that answers a transport hiccup with a second attempt instead of handing the
/// pipeline a dead bundle of tokens. What counts as a hiccup, and how long the backoff is, come from
/// <see cref="ModelRetryPolicy"/> — the same policy that governs embeddings, so the two can never
/// disagree about what is worth retrying.
/// </summary>
public sealed class RetryingChatClient : DelegatingChatClient
{
    private readonly int _retryCount;
    private readonly ILogger _logger;

    public RetryingChatClient(IChatClient innerClient, int retryCount, ILogger? logger = null)
        : base(innerClient)
    {
        _retryCount = Math.Max(0, retryCount);
        _logger = logger ?? NullLogger.Instance;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await InnerClient.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ModelRetryPolicy.IsTransient(ex) && attempt <= _retryCount && !cancellationToken.IsCancellationRequested)
            {
                var delay = ModelRetryPolicy.BackoffFor(attempt);
                _logger.LogWarning(
                    "Model chat call failed on attempt {Attempt}/{Attempts} ({Delay}s backoff): {Message}",
                    attempt,
                    _retryCount + 1,
                    delay.TotalSeconds,
                    ex.Message);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // The first chunk is where a refused connection shows itself, so the retry loop has to wrap
        // the enumeration rather than the call: it is checked again inside the iterator below.
        return StreamWithRetryAsync(messages, options, cancellationToken);
    }

    /// <summary>
    /// Yields the inner stream, restarting it when a transport failure lands before the first
    /// token. A failure *mid-stream* is not retried: the traveler has already read part of the
    /// answer, and repeating the prefix would show them the same half-sentence twice.
    /// </summary>
    private async IAsyncEnumerable<ChatResponseUpdate> StreamWithRetryAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var attempt = 0;

        while (true)
        {
            var enumerator = InnerClient.GetStreamingResponseAsync(messages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            var yielded = false;

            try
            {
                while (true)
                {
                    bool moved;
                    try
                    {
                        moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ModelRetryPolicy.IsTransient(ex) && !yielded && attempt < _retryCount && !cancellationToken.IsCancellationRequested)
                    {
                        attempt++;
                        var delay = ModelRetryPolicy.BackoffFor(attempt);
                        _logger.LogWarning(
                            "Model stream failed on attempt {Attempt}/{Attempts} ({Delay}s backoff): {Message}",
                            attempt,
                            _retryCount + 1,
                            delay.TotalSeconds,
                            ex.Message);

                        await enumerator.DisposeAsync().ConfigureAwait(false);
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    if (!moved)
                    {
                        yield break;
                    }

                    yielded = true;
                    yield return enumerator.Current;
                }
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
