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

    /// <summary>
    /// The attempt whose response is being validated right now. Lets a parser hold its final attempt
    /// to a looser standard, so a usable answer survives rather than costing the whole query.
    /// </summary>
    protected int CurrentAttempt { get; private set; }

    public abstract string AgentName { get; }

    public abstract string SanitizerModel { get; }

    protected async Task<T> RunJsonAsync<T>(
        string prompt,
        Func<JsonElement, ParseOutcome<T>> parse,
        CancellationToken cancellationToken = default,
        bool allowErrorObject = false)
    {
        var feedback = string.Empty;
        var raw = string.Empty;

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            CurrentAttempt = attempt;
            var attemptPrompt = prompt;
            if (attempt > 1)
            {
                attemptPrompt +=
                    $"\n\nYour previous response was invalid. Reason: {feedback}\nReturn ONLY valid JSON in the exact required format.";
            }

            var request = new LlmRequest(Model, attemptPrompt, JsonMode: true);

            try
            {
                raw = await Client
                    .CompleteAsync(request, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The transport failed — unreachable Ollama, timeout, missing model — which is the
                // usual cause of the pipeline's generic fallbacks. Logged here because this is the
                // only place that still knows which agent and model were being asked.
                Logger.LogError(
                    ex,
                    "{Agent} model call failed on attempt {Attempt}/{Attempts} (model {Model})",
                    AgentName,
                    attempt,
                    Attempts,
                    Model);
                throw;
            }

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
                // The raw response is the diagnosis, so it is logged at Warning rather than Debug:
                // the default log level hides Debug, and this is where "the model rambled" shows.
                Logger.LogWarning(
                    "{Agent} attempt {Attempt}/{Attempts} produced unusable JSON: {Message}. Raw response: {Raw}",
                    AgentName,
                    attempt,
                    Attempts,
                    ex.Message,
                    Truncate(raw));
                continue;
            }

            var outcome = parse(element);
            if (outcome.IsSuccess)
            {
                return outcome.Value!;
            }

            feedback = string.Join("; ", outcome.Errors);
            Logger.LogWarning(
                "{Agent} attempt {Attempt}/{Attempts} failed validation: {Feedback}. Raw response: {Raw}",
                AgentName,
                attempt,
                Attempts,
                feedback,
                Truncate(raw));
        }

        // The caller logs this exception, so the last raw response rides along: without it a Coolify
        // log shows that an agent failed but never shows what the model actually said.
        throw new Domain.Exceptions.AgentInvocationException(
            AgentName,
            $"failed after {Attempts} attempt(s): {feedback}; last raw response: {Truncate(raw)}");
    }

    /// <summary>Keeps a model response from flooding a log line or an exception message.</summary>
    private static string Truncate(string value, int max = 2000)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + $"… [truncated from {trimmed.Length} chars]";
    }
}
