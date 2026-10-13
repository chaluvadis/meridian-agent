namespace MeridianAgent.Infrastructure.AI.Agents;

using Microsoft.Extensions.AI;

/// <summary>
/// Shared plumbing for the four agents: prompt execution against a hosted model, JSON extraction
/// with the sanitizer fallback, and one validation-feedback retry before failing.
/// </summary>
public abstract class AgentBase(
    IChatClient chatClient,
    ILogger logger,
    AiOptions options,
    string? defaultModel = null,
    int attempts = AgentBase.DefaultAttempts)
{
    private const int DefaultAttempts = 2;

    protected IChatClient Client { get; } = chatClient;

    protected ILogger Logger { get; } = logger;

    /// <summary>The model settings this agent runs under, from the AI configuration section.</summary>
    protected AiOptions Options { get; } = options;

    /// <summary>
    /// The model this agent runs on. An explicit constructor argument wins (a test, or a caller that
    /// knows better); otherwise it is the configured <see cref="AiOptions.Model"/> — never a
    /// compile-time constant, because this value is what the gateway actually receives: a per-call
    /// <see cref="ChatOptions.ModelId"/> overrides whatever model the client holds.
    /// </summary>
    public string Model { get; } =
        string.IsNullOrWhiteSpace(defaultModel) ? options.Model : defaultModel;

    protected int Attempts { get; } = Math.Max(1, attempts);

    /// <summary>
    /// The attempt whose response is being validated right now. Lets a parser hold its final attempt
    /// to a looser standard, so a usable answer survives rather than costing the whole query.
    /// </summary>
    protected int CurrentAttempt { get; private set; }

    public abstract string AgentName { get; }

    /// <summary>
    /// Asks for a reply that is nothing but a JSON value. The response-format flag is set here, once,
    /// so a missing fence or a stray adjective becomes the model's problem rather than the parser's.
    /// Temperature and the reply cap come from configuration, so an operator can tune either.
    /// </summary>
    protected ChatOptions JsonOptions() => new()
    {
        ModelId = Model,
        Temperature = (float)Options.Temperature,
        MaxOutputTokens = Options.MaxTokens,
        ResponseFormat = ChatResponseFormat.Json,
    };

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

            try
            {
                var response = await Client
                    .GetResponseAsync(attemptPrompt, JsonOptions(), cancellationToken)
                    .ConfigureAwait(false);

                raw = response.Text ?? string.Empty;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The model call failed — an unreachable host, a timeout, a missing model, a rejected
                // API key — which is the usual cause of the pipeline's generic fallbacks. Logged here
                // because this is the only place that still knows which agent and model were asked.
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
                // The repair call uses the same model: there is no separate tiny model to keep warm,
                // and a capable model asked twice is still cheaper than a fallback. It takes the same
                // reply cap and temperature as the agents, so a rambling repair cannot run unbilled
                // and one knob cannot yield two behaviors in the same JSON path.
                element = await JsonExtractor
                    .ExtractWithLlmAsync(Client, Model, raw, cancellationToken, allowErrorObject, Options.MaxTokens, Options.Temperature)
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
        // log shows that an agent failed but never shows what the model actually answered.
        throw new Domain.Exceptions.AgentInvocationException(
            AgentName,
            $"failed after {Attempts} attempt(s): {feedback}; last raw response: {Truncate(raw)}");
    }

    /// <summary>
    /// Streams one prompt's reply as text pieces, for the Synthesizer's typed reveal. Text only:
    /// these prompts are prose, and a function call has nothing to add here. Pass <paramref name="json"/>
    /// for the one call whose reply is a JSON envelope the caller parses.
    /// </summary>
    protected async IAsyncEnumerable<string> StreamTextAsync(
        string prompt,
        double temperature,
        bool json = false,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var options = new ChatOptions
        {
            ModelId = Model,
            Temperature = (float)temperature,
            MaxOutputTokens = Options.MaxTokens,
        };

        if (json)
        {
            options.ResponseFormat = ChatResponseFormat.Json;
        }

        await foreach (var update in Client
            .GetStreamingResponseAsync(prompt, options, cancellationToken)
            .ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    /// <summary>Keeps a model response from flooding a log line or an exception message.</summary>
    private static string Truncate(string value, int max = 2000)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + $"… [truncated from {trimmed.Length} chars]";
    }
}
