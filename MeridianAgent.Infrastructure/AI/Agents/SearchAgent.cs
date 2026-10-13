namespace MeridianAgent.Infrastructure.AI.Agents;

/// <summary>
/// Agent 2: proposes 1-3 venues that fit the extracted constraints AND the current reality
/// (weather, local time, energy). Model: gpt-4o-mini.
/// </summary>
public sealed class SearchAgent(
    IChatClient chatClient,
    ILogger<SearchAgent> logger,
    IOptions<AiOptions>? aiOptions = null,
    string? model = null) : AgentBase(chatClient, logger, aiOptions?.Value ?? new AiOptions(), model), ISearchAgent
{
    public const string DefaultModel = AiOptions.DefaultModel;

    /// <summary>
    /// The one phrasing the Search Agent is taught for a mixed-venue trade, and the same one the
    /// retry feedback offers when a compromise is too vague to weigh. The prompt, the feedback
    /// message, and the tests all read this constant, so an edit that turns the example into a
    /// phrasing <see cref="NamesTheShelter"/> rejects is caught here rather than costing the model
    /// a retry attempt on every query that negotiates.
    /// </summary>
    public const string MixedVenueTradeExample =
        "A mixed venue rather than the fully indoor one you asked for — its indoor section is air-conditioned and the courtyard is shaded";

    private const string Prompt = """
        You are a hyper-personalized, resourceful travel planning agent. Your job is to find the BEST POSSIBLE match for the user, even when a 100% perfect match does not exist.

        PHILOSOPHY: "Reality hits different." If the weather is extreme, the user is tired, or it's late at night, you MUST adapt. Never suggest something that would make the user's experience worse. A refusal is a failure of your job; an imperfect venue described honestly is the job done well.

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

        STRICT RULES FOR CONSTRAINTS:
        1. HARD constraints are absolute and can never be broken: relevant_dietary (e.g. vegetarian, halal), safety rules, and avoid_list (e.g. "no crowded places"). Never propose a venue that violates one — that is the only reason you may refuse.
        2. SOFT constraints are preferences, not filters: venue type (indoor/outdoor/mixed), exact travel time, specific vibe, pacing, energy, budget. You may bend any of them, but you MUST name what you bent in "compromise".
        3. THE "MIXED" VENUE RULE (crucial): if the user prefers "indoor" but the only nearby options are "mixed" or "outdoor", YOU MUST evaluate whether the mixed venue has a shaded, covered, or air-conditioned section. When it does, treat it as a VALID MATCH — propose it and state the compromise. A mixed venue with a sheltered indoor section satisfies an indoor preference; only reject it when it offers no shelter at all, or when the weather override below applies.
        4. WEATHER OVERRIDE: strictly enforce indoor/sheltered ONLY when weather_temp_c > 38 OR is_raining = true. Below that, a mixed venue is perfectly acceptable for an indoor preference. Never enforce indoor-only just because the user usually prefers it.
        5. REALITY CHECK:
           - If local_time is between 23:00 and 06:00 → only suggest venues open at that hour.
           - If energy_level = "low" → prioritize venues within 10 minutes travel, minimal walking.

        BEST EFFORT LOGIC:
        - First, try to find a perfect match.
        - If no perfect match exists, propose the CLOSEST POSSIBLE MATCH that respects ALL HARD constraints, and say in "compromise" exactly which soft preference you bent and how.
          * State the trade in one of these forms, never anything vaguer:
            - a measured difference — "It is 15 mins away instead of your preferred 10 mins", "about 20 dirhams more per head"
            - a named swap — "Italian rather than the Japanese you asked for", "$MixedVenueTradeExample"
          * Never write "slightly further out", "a bit pricier", "close enough": the traveler has to be able to accept or refuse the trade, and cannot weigh a hedge.
          * Set "compromise" to null only when the match is exact.
        - NEGOTIATE, DO NOT REFUSE: you MUST return 1 to 3 venues. Return the error form ONLY when no venue in the entire nearby list satisfies the HARD constraints (for example, a vegetarian request in an area with nothing but meat-focused stalls). Never return it because the setting, vibe, travel time, or budget was not perfect — that is a compromise to describe, not a dead end.
        - Never propose more than 3 venues.

        OUTPUT FORMAT (exact JSON array):
        [
          {
            "venue_name": "string",
            "venue_type": "indoor | outdoor | mixed",
            "category": "restaurant | cafe | museum | park | market | activity",
            "travel_time_minutes": 0,
            "why_it_fits": "One concise sentence explaining why this is the best available option right now.",
            "compromise": "string | null — the soft preference you relaxed to make this work, or null if it fits exactly.",
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
                ["MixedVenueTradeExample"] = MixedVenueTradeExample,
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
            "instead of your preferred 10 mins'), a named swap ('Italian rather than the Japanese " +
            "you asked for'), or a mixed-venue trade naming its shelter ('" + MixedVenueTradeExample +
            $"'); '{text}' says something was relaxed but not what");

        return text;
    }

    /// <summary>
    /// Whether a compromise names the difference it made: a quantity, an explicit "X rather than
    /// Y" swap, or a mixed venue that says which part of it is sheltered. Vague hedges ("slightly
    /// further out") fail, so the traveler is never asked to accept a trade they cannot weigh.
    /// </summary>
    private static bool StatesTheDifference(string compromise) =>
        (compromise.Length >= 15 && compromise.Any(char.IsDigit)) ||
        compromise.Contains(" instead of ", StringComparison.OrdinalIgnoreCase) ||
        compromise.Contains(" rather than ", StringComparison.OrdinalIgnoreCase) ||
        NamesTheShelter(compromise);

    /// <summary>
    /// Whether a mixed-venue trade says what its sheltered part actually is — an air-conditioned,
    /// shaded, or covered section. "A mixed venue, but fine in this weather" is still a hedge; the
    /// amenity is what makes the trade weighable.
    /// </summary>
    private static bool NamesTheShelter(string compromise) =>
        compromise.Contains("mixed", StringComparison.OrdinalIgnoreCase) &&
        (compromise.Contains("air-conditioned", StringComparison.OrdinalIgnoreCase) ||
         compromise.Contains("air conditioned", StringComparison.OrdinalIgnoreCase) ||
         compromise.Contains("shaded", StringComparison.OrdinalIgnoreCase) ||
         compromise.Contains("covered", StringComparison.OrdinalIgnoreCase));
}
