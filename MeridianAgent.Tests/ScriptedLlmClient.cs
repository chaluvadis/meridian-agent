namespace MeridianAgent.Tests;

/// <summary>Deterministic ILlmClient: pops scripted responses and records every request.</summary>
public sealed class ScriptedLlmClient : ILlmClient
{
    private readonly Queue<string> _responses = new();

    public ScriptedLlmClient(params string[] responses)
    {
        foreach (var response in responses)
        {
            _responses.Enqueue(response);
        }
    }

    public List<LlmRequest> Calls { get; } = new();

    public Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add(request);
        return _responses.Count == 0
            ? throw new InvalidOperationException("ScriptedLlmClient has no responses left.")
            : Task.FromResult(_responses.Dequeue());
    }

    public async IAsyncEnumerable<string> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls.Add(request);
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("ScriptedLlmClient has no responses left.");
        }

        var response = _responses.Dequeue();
        foreach (var chunk in response.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            await Task.Yield();
            yield return chunk + " ";
        }
    }

    public Task<float[]> EmbedAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add(request);
        var vector = new float[768];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)((i % 11) * 0.05);
        }

        return Task.FromResult(vector);
    }
}
