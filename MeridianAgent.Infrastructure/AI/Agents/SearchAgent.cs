namespace MeridianAgent.Infrastructure.AI.Agents;

/// <summary>
/// Agent 2: proposes 1-3 venues that fit the extracted constraints AND the current reality
/// (weather, local time, energy). Model: qwen2.5:3b.
/// </summary>
public sealed class SearchAgent(
    ILlmClient client,
    ILogger<SearchAgent> logger,
    string? model = null,
    string? sanitizerModel = null) : AgentBase(client, logger, model ?? DefaultModel), ISearchAgent
{
    public const string DefaultModel = "qwen2.5:3b";

    private const string Prompt = """
        You are a hyper-personalized travel planning agent. Your job is to propose 1 to 3 venues or activities that perfectly match the user's extracted constraints AND the current reality of their environment.

        PHILOSOPHY: "Reality hits different." If the weather is extreme, the user is tired, or it's late at night, you MUST adapt. Never suggest something that would make the user's experience worse.

        <extracted_constraints>
        $ExtractedConstraintsJson
        </extracted_constraints>

        <current_reality>
        {
          "location": "$CurrentLocation",
          "local_time": "$LocalTime",
          "weather_temp_c": $Temperature,
          "weather_condition": "$WeatherCondition",
          "is_raining": $IsRaining,
          "nearby_venues": $NearbyVenuesJson
        }
        </current_reality>

        STRICT RULES:
        1. Output ONLY a valid JSON array. No markdown, no explanations.
        2. REALITY CHECK:
           - If temperature > 35°C OR is_raining = true → ALL suggestions MUST be indoor/sheltered.
           - If local_time is between 23:00 and 06:00 → only suggest venues open at that hour.
           - If energy_level = "low" → prioritize venues within 10 minutes travel, minimal walking.
        3. PERSONALIZATION CHECK:
           - HARD constraints are absolute: relevant_dietary and avoid_list. Never compromise on these, ever.
           - SOFT constraints (relevant_interests, pacing_preference, energy_level, time_sensitivity, budget_range, travel time) are preferences, not filters. Never discard an otherwise strong venue just because one of them is unmet.
        4. NEGOTIATE, DO NOT REFUSE: a perfect match is rare and refusing is a failure of your job. When nothing satisfies every soft constraint, propose the CLOSEST POSSIBLE MATCH and state in "compromise" exactly which preference you bent and by how much.
           - State the trade in one of two forms, never anything vaguer:
             * a measured difference — "It is 15 mins away instead of your preferred 10 mins", "about 20 dirhams more per head"
             * a named swap — "Italian rather than the Japanese you asked for"
           - Never write "slightly further out", "a bit pricier", "close enough": the traveler has to be able to accept or refuse the trade, and cannot weigh a hedge.
           - Set "compromise" to null only when the match is exact.
        5. Propose between 1 and 3 options. Never more than 3.

        OUTPUT FORMAT (exact):
        [
          {
            "venue_name": "string",
            "venue_type": "indoor | outdoor | mixed",
            "category": "restaurant | cafe | museum | park | market | activity",
            "travel_time_minutes": 0,
            "why_it_fits": "One concise sentence explaining why this is the best option available right now.",
            "compromise": "string | null — the soft constraint you relaxed to make this work, or null if it fits exactly.",
            "estimated_duration": "string",
            "best_time_to_visit": "string"
          }
        ]

        Reserve the error form for a genuine dead end only — when no venue in the area satisfies the HARD constraints:
        {
          "error": true,
          "reason": "string naming the fundamental conflict (e.g., 'You need vegetarian and quiet, but everything nearby is loud, meat-focused street food')"
        }
        """;

    /// <summary>
    /// Authorizes the model to use its own knowledge when the nearby-venue list is empty. Without
    /// it a bare list reads as "there is nothing here" and the agent refuses instead of negotiating.
    /// </summary>
    private const string NoVenuesSuppliedNote = """
        NOTE: The nearby_venues list above is empty. You are authorized to suggest well-known, real venues in the area named in current_reality that fit the constraints, using your general knowledge of the place.
        """;

    public override string AgentName => "SearchAgent";

    public override string SanitizerModel { get; } = sanitizerModel ?? DefaultModel;

    public Task<ProposedItinerary> ProposeAsync(
        ExtractedConstraints constraints,
        TravelRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = request.Context;
        var prompt = PromptRenderer.Render(
            Prompt,
            new Dictionary<string, string>
            {
                ["ExtractedConstraintsJson"] = JsonElementReader.Serialize(constraints),
                ["CurrentLocation"] = context?.Location ?? request.Profile.Destination,
                ["LocalTime"] = context?.LocalTime ?? "unknown",
                ["Temperature"] = JsonElementReader.ToInvariant(context?.TemperatureC ?? 0),
                ["WeatherCondition"] = context?.WeatherCondition ?? "unknown",
                ["IsRaining"] = JsonElementReader.ToJsonLiteral(context?.IsRaining ?? false),
                ["NearbyVenuesJson"] = JsonElementReader.Serialize(request.NearbyVenues),
            });

        // An empty list otherwise reads as "there is nothing here" and the model refuses.
        if (request.NearbyVenues.Count == 0)
        {
            prompt += $"\n\n{NoVenuesSuppliedNote}";
        }

        return RunJsonAsync(
            prompt,
            element => ParseItinerary(element),
            cancellationToken,
            allowErrorObject: true);
    }

    private ParseOutcome<ProposedItinerary> ParseItinerary(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            JsonExtractor.TryGetErrorMessage(root, out var reason))
        {
            throw new NoVenuesFoundException(reason ?? "the search agent found nothing that fits");
        }

        var items = ToVenueElements(root);

        if (items is null)
        {
            return ParseOutcome<ProposedItinerary>.Fail("expected a JSON array of venues");
        }

        if (items.Count == 0)
        {
            throw new NoVenuesFoundException("nothing nearby fits your non-negotiables");
        }

        var errors = new List<string>();
        var venues = new List<Venue>();

        var index = 0;
        foreach (var item in items)
        {
            index++;
            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"venue #{index} must be a JSON object");
                continue;
            }

            var name = JsonElementReader.ReadString(item, "venue_name", errors, required: true);
            var type = JsonElementReader.ReadEnum<VenueType>(item, "venue_type", errors, required: true);
            var category = JsonElementReader.ReadEnum<VenueCategory>(item, "category", errors, required: true);
            if (name is null || type is null || category is null)
            {
                continue;
            }

            venues.Add(new Venue
            {
                Name = name,
                VenueType = type.Value,
                Category = category.Value,
                TravelTimeMinutes = JsonElementReader.ReadInt(item, "travel_time_minutes", errors),
                WhyItFits = JsonElementReader.ReadString(item, "why_it_fits", errors, required: false) ?? string.Empty,
                Compromise = ReadCompromise(item, index, errors),
                EstimatedDuration = JsonElementReader.ReadString(item, "estimated_duration", errors, required: false) ?? string.Empty,
                BestTimeToVisit = JsonElementReader.ReadString(item, "best_time_to_visit", errors, required: false) ?? string.Empty,
            });

            if (venues.Count > 3)
            {
                errors.Add("at most 3 venues may be proposed");
                break;
            }
        }

        if (errors.Count > 0)
        {
            return ParseOutcome<ProposedItinerary>.Fail(errors.ToArray());
        }

        if (venues.Count == 0)
        {
            throw new NoVenuesFoundException("the proposed plan contained no venues");
        }

        return ParseOutcome<ProposedItinerary>.Ok(new ProposedItinerary(venues));
    }

    /// <summary>
    /// The venues to read, whether the model sent the array it was asked for or a single object.
    /// Small models routinely answer "one option" with the object rather than a one-element array,
    /// and throwing that away means a perfectly usable venue never reaches the traveler.
    /// </summary>
    private static IReadOnlyList<JsonElement>? ToVenueElements(JsonElement root) => root.ValueKind switch
    {
        JsonValueKind.Array => root.EnumerateArray().ToArray(),
        JsonValueKind.Object => new[] { root },
        _ => null,
    };

    /// <summary>
    /// The negotiated concession the model declared, or null. A blank string is treated as nothing
    /// rather than an empty "compromise" the Synthesizer would then have to explain. A trade the
    /// traveler could not accept or refuse is reported instead of kept — but only while an attempt
    /// remains, because a venue with a loosely worded trade still beats no plan at all.
    /// </summary>
    private string? ReadCompromise(JsonElement item, int index, List<string> errors)
    {
        if (!item.TryGetProperty("compromise", out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = property.GetString()?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (StatesTheDifference(text) || CurrentAttempt >= Attempts)
        {
            return text;
        }

        errors.Add(
            $"venue #{index} compromise must state the trade, as a measurement ('It is 15 mins away " +
            "instead of your preferred 10 mins') or a named swap ('Italian rather than the Japanese " +
            $"you asked for'); '{text}' says something was relaxed but not what");

        return text;
    }

    /// <summary>
    /// Whether a compromise names the difference it made: a quantity, or an explicit "X rather than
    /// Y" swap. Vague hedges ("slightly further out") fail, so the traveler is never asked to accept
    /// a trade they cannot weigh.
    /// </summary>
    private static bool StatesTheDifference(string compromise) =>
        (compromise.Length >= 15 && compromise.Any(char.IsDigit)) ||
        compromise.Contains(" instead of ", StringComparison.OrdinalIgnoreCase) ||
        compromise.Contains(" rather than ", StringComparison.OrdinalIgnoreCase);
}
