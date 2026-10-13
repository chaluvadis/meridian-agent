namespace MeridianAgent.Tests;

using Microsoft.Extensions.AI;

/// <summary>
/// Deterministic IChatClient: pops scripted responses and records every call, so a test can assert
/// what an agent asked for (prompt text, model, JSON mode) without a hosted model in the loop.
/// </summary>
public sealed class ScriptedLlmClient : IChatClient
{
    private readonly Queue<string> _responses = new();

    public ScriptedLlmClient(params string[] responses)
    {
        foreach (var response in responses)
        {
            _responses.Enqueue(response);
        }
    }

    /// <summary>One recorded call: the prompt sent and what the call asked the model for.</summary>
    public sealed record Call(string Prompt, string? Model, bool JsonMode, double? Temperature, int? MaxTokens);

    public List<Call> Calls { get; } = new();

    /// <summary>Text the next streamed call returns (overrides the scripted queue for one call).</summary>
    public string Response { get; set; } = "Top pick: Karak House.";

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(Record(messages, options));

        return _responses.Count == 0
            ? throw new InvalidOperationException("ScriptedLlmClient has no responses left.")
            : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _responses.Dequeue())));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls.Add(Record(messages, options));

        // Nothing scripted: stream the Response text instead of exhausting the queue.
        var text = _responses.Count == 0 ? Response : _responses.Dequeue();

        foreach (var chunk in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk + " ");
        }
    }

    public object? GetService(Type key, object? serviceKey = null) => null;

    private Call Record(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var prompt = messages.LastOrDefault(message => message.Role == ChatRole.User)?.Text ?? string.Empty;

        return new Call(
            prompt,
            options?.ModelId,
            options?.ResponseFormat is ChatResponseFormatJson,
            (double?)options?.Temperature,
            options?.MaxOutputTokens);
    }

    /// <summary>Nothing is held open, so there is nothing to dispose either.</summary>
    public void Dispose()
    {
    }
}
