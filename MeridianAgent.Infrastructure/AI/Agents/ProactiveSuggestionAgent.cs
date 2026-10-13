namespace MeridianAgent.Infrastructure.AI.Agents;

/// <summary>
/// The unprompted counterpart of the pipeline: reads the traveler's profile, the live context, and the
/// venues around them, then proposes 0-3 ideas worth surfacing without being asked. Model: gpt-4o-mini.
/// </summary>
public sealed class ProactiveSuggestionAgent(
    IChatClient chatClient,
    ILogger<ProactiveSuggestionAgent> logger,
    IOptions<AiOptions>? aiOptions = null,
    string? model = null) : AgentBase(chatClient, logger, aiOptions?.Value ?? new AiOptions(), model), IProactiveSuggestionAgent
{
    public const string DefaultModel = AiOptions.DefaultModel;

    private const string Prompt = """
        You are the intuition engine of The Meridian Agent. The traveler has NOT asked anything — you noticed a good moment and speak first.

        <user_profile>
        $UserProfileJson
        </user_profile>

        <current_context>
        {
          "location": "$CurrentLocation",
          "local_time": "$LocalTime",
          "temperature_c": "$Temperature",
          "weather": "$WeatherCondition",
          "is_raining": "$IsRaining"
        }
        </current_context>

        <nearby_venues>
        $NearbyVenuesJson
        </nearby_venues>

        <rulebook_warnings>
        $RulebookJson
        </rulebook_warnings>

        STRICT RULES:
        1. Output ONLY valid JSON. No markdown, no explanations.
        2. Propose at most 3 suggestions, ranked by how strongly they fit THIS moment (time, weather, energy, location).
        3. ONLY suggest venues from the nearby_venues list. Never invent venues.
        4. Respect dietary_rules and avoid_list absolutely.
        5. NEVER propose anything flagged by the rulebook (for example venues or activities with a HIGH severity rule).
        6. If nothing genuinely fits this exact moment, output an empty suggestions array. A quiet empty beat is better than a bad idea.
        7. Titles are 3-8 words, lowercase, phrased as an invitation not a command ("a quiet rooftop before it gets busy").
        8. Rationale is ONE short sentence explaining why it fits the current moment specifically. No generic travel advice.
        9. confidence_score is a number 0.0-1.0: how confident you are that this suggestion lands well right now.

        OUTPUT FORMAT (exact):
        {
          "suggestions": [
            {
              "title": "string",
              "rationale": "string",
              "confidence_score": 0.0-1.0,
              "suggested_timing": "string | null",
              "venue_type": "string | null",
              "interest": "string | null"
            }
          ]
        }
        """;

    public override string AgentName => "ProactiveSuggestionAgent";

    public Task<IReadOnlyList<ProactiveSuggestion>> GenerateAsync(
        ProactiveRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = request.Context;
        var prompt = PromptRenderer.Render(
            Prompt,
            new Dictionary<string, string>
            {
                ["UserProfileJson"] = JsonElementReader.Serialize(request.Profile),
                ["CurrentLocation"] = context?.Location ?? request.Profile.Destination,
                ["LocalTime"] = context?.LocalTime ?? "unknown",
                ["Temperature"] = JsonElementReader.ToInvariant(context?.TemperatureC ?? 0),
                ["WeatherCondition"] = context?.WeatherCondition ?? "unknown",
                ["IsRaining"] = JsonElementReader.ToJsonLiteral(context?.IsRaining ?? false),
                ["NearbyVenuesJson"] = JsonElementReader.Serialize(request.NearbyVenues),
                ["RulebookJson"] = JsonElementReader.Serialize(request.Rulebook),
            });

        return RunJsonAsync(prompt, ParseSuggestions, cancellationToken);
    }

    private static ParseOutcome<IReadOnlyList<ProactiveSuggestion>> ParseSuggestions(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return ParseOutcome<IReadOnlyList<ProactiveSuggestion>>.Fail("expected a JSON object with a suggestions array");
        }

        if (root.TryGetProperty("suggestions", out var suggestionProperty) &&
            (suggestionProperty.ValueKind == JsonValueKind.Null || suggestionProperty.ValueKind == JsonValueKind.Undefined))
        {
            return ParseOutcome<IReadOnlyList<ProactiveSuggestion>>.Ok(Array.Empty<ProactiveSuggestion>());
        }

        if (suggestionProperty.ValueKind != JsonValueKind.Array)
        {
            return ParseOutcome<IReadOnlyList<ProactiveSuggestion>>.Fail("missing required field 'suggestions'");
        }

        var errors = new List<string>();
        var suggestions = new List<ProactiveSuggestion>();
        var index = 0;

        foreach (var item in suggestionProperty.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"suggestions[{index}] must be an object");
                index++;
                continue;
            }

            var title = JsonElementReader.ReadString(item, "title", errors, required: true);
            var rationale = JsonElementReader.ReadString(item, "rationale", errors, required: true);
            var confidence = JsonElementReader.ReadDouble(item, "confidence_score", errors, fallback: 0);
            var timing = JsonElementReader.ReadString(item, "suggested_timing", errors, required: false);
            var venueType = JsonElementReader.ReadString(item, "venue_type", errors, required: false);
            var interest = JsonElementReader.ReadString(item, "interest", errors, required: false);
            index++;

            if (title is null || rationale is null)
            {
                continue;
            }

            if (confidence is < 0 or > 1)
            {
                errors.Add($"suggestions[{index - 1}].confidence_score must be between 0.0 and 1.0");
                continue;
            }

            suggestions.Add(new ProactiveSuggestion(
                title,
                rationale,
                confidence,
                timing,
                venueType,
                interest));
        }

        if (errors.Count > 0)
        {
            return ParseOutcome<IReadOnlyList<ProactiveSuggestion>>.Fail(errors.ToArray());
        }

        return ParseOutcome<IReadOnlyList<ProactiveSuggestion>>.Ok(
            suggestions.OrderByDescending(s => s.ConfidenceScore).ToArray());
    }
}
