namespace MeridianAgent.Infrastructure.AI;

using System.ClientModel;
using System.ClientModel.Primitives;

/// <summary>
/// One retry policy for every model call, shared by the chat client and the embedding generator.
/// It existed once as a Polly policy in the old Ollama client, and it stays one thing: chat,
/// embeddings, and the JSON repair fallback all classify a failure and back off the same way, so
/// adding a transient status code or changing the backoff cannot silently apply to only one of them.
/// </summary>
internal static class ModelRetryPolicy
{
    /// <summary>
    /// A failure worth another attempt: a refused connection, an abandoned request, or a rate limit
    /// or server-side status. A 401 or a 404 is a configuration problem and must fail fast — it is
    /// the caller's error, not the network's.
    /// </summary>
    public static bool IsTransient(Exception ex) =>
        ex is HttpRequestException
            or TaskCanceledException
            or ClientResultException { Status: 429 }
            or ClientResultException { Status: >= 500 };

    /// <summary>Exponential, capped: doubling up to 30s, so a long outage does not queue forever.</summary>
    public static TimeSpan BackoffFor(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
}

/// <summary>
/// Replaces the retry policy the OpenAI SDK installs by default. The SDK's own policy already
/// retries a call three times, which stacked under <see cref="RetryingChatClient"/> turns one
/// logical call into nine HTTP requests with compounded backoff under exactly the rate-limit
/// conditions the outer retry exists for. This pass-through policy leaves the retry budget to the
/// wrapper: one policy, one exponential ladder, one number of attempts.
/// </summary>
internal sealed class SingleAttemptPolicy : PipelinePolicy
{
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex) =>
        ProcessNext(message, pipeline, currentIndex);

    public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex) =>
        ProcessNextAsync(message, pipeline, currentIndex);
}
