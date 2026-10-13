namespace MeridianAgent.Infrastructure.AI.Agents;

/// <summary>
/// Agent 1: extracts ONLY the constraints that matter for the current query from the user's profile.
/// Model: gpt-4o-mini - cheap and fast, because this stage is pure data reduction.
/// </summary>
public sealed class ContextAgent(
    IChatClient chatClient,
    ILogger<ContextAgent> logger,
    IOptions<AiOptions>? aiOptions = null,
    string? model = null)
    : AgentBase(chatClient, logger, aiOptions?.Value ?? new AiOptions(), model), IContextAgent
{
    public const string DefaultModel = AiOptions.DefaultModel;

    private const string Prompt = """
        You are a strict data-extraction agent. Your ONLY job is to read the user's profile and their current query, then output a JSON object containing ONLY the constraints relevant to this specific moment.

        <user_profile>
        $UserProfileJson
        </user_profile>

        <current_query>
        $UserQuery
        </current_query>

        <current_context>
        {
          "location": "$CurrentLocation",
          "local_time": "$LocalTime",
          "weather": "$WeatherSummary"
        }
        </current_context>

        STRICT RULES:
        1. Output ONLY valid JSON. No markdown, no explanations, no commentary.
        2. Include ONLY preferences that matter for THIS query. If the user asks about food, ignore their photography preferences. If they ask about sightseeing, ignore their dietary rules.
        3. If a field is not relevant, set it to null or an empty array.
        4. Never invent preferences. If the profile doesn't mention something, leave it null.
        5. The "energy_level" must be inferred from the query tone (e.g., "I'm exhausted" → "low", "Let's explore!" → "high"). Default to "medium" if unclear.

        OUTPUT FORMAT (exact):
        {
          "query_intent": "food | sightseeing | transport | shopping | nightlife | relaxation | emergency",
          "relevant_dietary": ["string"] | null,
          "relevant_interests": ["string"] | null,
          "avoid_list": ["string"] | null,
          "pacing_preference": "slow | moderate | fast",
          "energy_level": "low | medium | high",
          "time_sensitivity": "urgent | normal | flexible",
          "budget_range": "low | medium | high | null"
        }
        """;

    public override string AgentName => "ContextAgent";

    public Task<ExtractedConstraints> ExtractAsync(
        TravelRequest request,
        CancellationToken cancellationToken = default)
    {
        // The profile is the one input a traveler cannot re-type mid-conversation, so a serialization
        // failure here has to be visible with the exact exception rather than surfacing as the
        // orchestrator's generic "trouble reading your preferences" fallback.
        string prompt;
        try
        {
            var profile = (request.Profile ?? UserProfile.Default).WithSafeLists();
            var profileJson = JsonElementReader.Serialize(profile);

            Logger.LogInformation(
                "Profile serialized for the Context Agent: {Length} chars, model {Model}",
                profileJson.Length,
                Model);

            prompt = PromptRenderer.Render(
                Prompt,
                new Dictionary<string, string>
                {
                    ["UserProfileJson"] = profileJson,
                    ["UserQuery"] = request.Query,
                    ["CurrentLocation"] = request.Context?.Location ?? profile.Destination,
                    ["LocalTime"] = request.Context?.LocalTime ?? "unknown",
                    ["WeatherSummary"] = request.Context?.WeatherSummary ?? "unknown",
                });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(
                ex,
                "Could not serialize the traveler profile (destination {Destination}) for the Context Agent",
                request.Profile?.Destination);
            throw new InvalidOperationException("the traveler profile could not be serialized for the context stage", ex);
        }

        return RunJsonAsync(prompt, element =>
        {
            var outcome = ParseConstraints(element);
            if (outcome.IsSuccess)
            {
                Logger.LogDebug(
                    "Context agent extracted: {Constraints}",
                    JsonElementReader.Serialize(outcome.Value));
            }

            return outcome;
        }, cancellationToken);
    }

    private static ParseOutcome<ExtractedConstraints> ParseConstraints(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return ParseOutcome<ExtractedConstraints>.Fail("expected a JSON object");
        }

        var errors = new List<string>();

        var intent = JsonElementReader.ReadEnum<QueryIntent>(root, "query_intent", errors, required: true);
        if (intent is null)
        {
            return ParseOutcome<ExtractedConstraints>.Fail(errors.ToArray());
        }

        var dietary = JsonElementReader.ReadStringList(root, "relevant_dietary", errors);
        var interests = JsonElementReader.ReadStringList(root, "relevant_interests", errors);
        var avoid = JsonElementReader.ReadStringList(root, "avoid_list", errors);
        var pacing = JsonElementReader.ReadEnum<PacingPreference>(root, "pacing_preference", errors, required: false) ?? PacingPreference.Moderate;
        var energy = JsonElementReader.ReadEnum<EnergyLevel>(root, "energy_level", errors, required: false) ?? EnergyLevel.Medium;
        var timeSensitivity = JsonElementReader.ReadEnum<TimeSensitivity>(root, "time_sensitivity", errors, required: false) ?? TimeSensitivity.Normal;
        var budget = JsonElementReader.ReadEnum<BudgetRange>(root, "budget_range", errors, required: false);

        if (errors.Count > 0)
        {
            return ParseOutcome<ExtractedConstraints>.Fail(errors.ToArray());
        }

        return ParseOutcome<ExtractedConstraints>.Ok(new ExtractedConstraints
        {
            QueryIntent = intent.Value,
            RelevantDietary = dietary,
            RelevantInterests = interests,
            AvoidList = avoid,
            PacingPreference = pacing,
            EnergyLevel = energy,
            TimeSensitivity = timeSensitivity,
            BudgetRange = budget,
        });
    }
}
