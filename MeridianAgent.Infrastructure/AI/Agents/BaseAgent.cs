namespace MeridianAgent.Infrastructure.AI.Agents;

/// <summary>
/// Shared plumbing for the four agents: prompt execution against a model, JSON extraction with the
/// sanitizer fallback, and one validation-feedback retry before failing.
/// </summary>
public abstract class AgentBase(ILlmClient client, ILogger logger, string defaultModel, int attempts = AgentBase.DefaultAttempts)
{
    private const int DefaultAttempts = 2;

    protected ILlmClient Client { get; } = client;

    protected ILogger Logger { get; } = logger;

    /// <summary>The Ollama model tag this agent runs on.</summary>
    public string Model { get; } = defaultModel;

    protected int Attempts { get; } = Math.Max(1, attempts);

    public abstract string AgentName { get; }

    public abstract string SanitizerModel { get; }

    protected async Task<T> RunJsonAsync<T>(
        string prompt,
        Func<JsonElement, ParseOutcome<T>> parse,
        CancellationToken cancellationToken = default,
        bool allowErrorObject = false)
    {
        var feedback = string.Empty;

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var attemptPrompt = prompt;
            if (attempt > 1)
            {
                attemptPrompt +=
                    $"\n\nYour previous response was invalid. Reason: {feedback}\nReturn ONLY valid JSON in the exact required format.";
            }

            var raw = await Client
                .CompleteAsync(new LlmRequest(Model, attemptPrompt, JsonMode: true), cancellationToken)
                .ConfigureAwait(false);

            Logger.LogDebug("{Agent} raw response (model {Model}): {Raw}", AgentName, Model, raw);

            JsonElement element;
            try
            {
                element = await JsonExtractor
                    .ExtractWithLlmAsync(Client, SanitizerModel, raw, cancellationToken, allowErrorObject)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                feedback = ex.Message;
                Logger.LogWarning("{Agent} attempt {Attempt} produced unusable JSON: {Message}", AgentName, attempt, ex.Message);
                continue;
            }

            var outcome = parse(element);
            if (outcome.IsSuccess)
            {
                return outcome.Value!;
            }

            feedback = string.Join("; ", outcome.Errors);
            Logger.LogWarning("{Agent} attempt {Attempt} failed validation: {Feedback}", AgentName, attempt, feedback);
        }

        throw new Domain.Exceptions.AgentInvocationException(AgentName, $"failed after {Attempts} attempt(s): {feedback}");
    }
}
