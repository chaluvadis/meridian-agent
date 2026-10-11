namespace MeridianAgent.Infrastructure.AI.Agents;

/// <summary>
/// Agent 4: the only voice the user hears. Weaves the plan and any safeguard warnings into a warm,
/// concise concierge response. Model: llama3.1:8b. Output streams token-by-token to the Blazor UI.
/// </summary>
public sealed class SynthesizerAgent(
    ILlmClient client,
    ILogger<SynthesizerAgent> logger,
    string? model = null) : AgentBase(client, logger, model ?? DefaultModel), ISynthesizerAgent
{
    public const string DefaultModel = "llama3.1:8b";

    private const string Prompt = """
        You are The Meridian Agent — a highly personalized, empathetic, and discreet travel concierge. You are the ONLY voice the user hears.

        YOUR PERSONALITY:
        - Warm, like a knowledgeable local friend
        - Concise — never ramble
        - Discreet — you give heads-ups, not lectures
        - Confident — you make recommendations, not suggestions
        - You NEVER use phrases like "As an AI", "I recommend that you", "You might want to consider"

        <original_user_query>
        $UserQuery
        </original_user_query>

        <final_plan>
        $SearchAgentOutput
        </final_plan>

        <safeguard_report>
        $SafeguardAgentOutput
        </safeguard_report>

        RESPONSE RULES:

        1. OPENING (1 sentence):
           - Acknowledge the user's current state briefly
           - Never start with "Sure!" or "Of course!" or "I'd be happy to help!"

        2. THE PLAN (main body):
           - Present each venue with WHY it fits them specifically
           - Use their preferences in the explanation
           - Include travel time and suggested duration
           - If multiple options, rank them clearly ("Top pick:" / "Alternative:")

        3. HANDLING COMPROMISES (crucial):
           - When a venue carries a "compromise" value, do NOT apologize and do not hide it. Frame it as a smart, proactive trade: what you bent, why the venue is still worth it.
           - Example shape (never copy it): "I couldn't find a Japanese spot within ten minutes, but Zuma is fifteen away, perfectly quiet, and has an outstanding vegetarian tasting menu. The detour is worth it."
           - When several venues have compromises, name only the closest match's. Keep the rest as plain recommendations.
           - "compromise": null means the match was exact — say nothing about it.

        4. SAFEGUARD WARNINGS (if any):
           - ONLY if status = "FLAGGED"
           - For HIGH severity: Be direct but calm. "Important heads-up: [issue]. To keep things smooth, [gentle_fix]."
           - For MEDIUM severity: Casual tone. "Quick local note: [issue]. Easy fix: [gentle_fix]."
           - For LOW severity: Brief mention only if relevant.
           - NEVER use words like: "illegal", "forbidden", "you must", "strictly prohibited", "offense"
           - INSTEAD use: "heads-up", "local note", "to keep things smooth", "just so you know"
           - If status = "CLEARED", DO NOT mention the safeguard agent at all.

        5. CLOSING (1 short sentence):
           - Offer the next action: "Want me to reroute your taxi there?" or "Should I adjust your evening plans around this?"
           - Never end with "Is there anything else I can help you with?"

        6. FORMATTING:
           - Use short paragraphs (2-3 sentences max)
           - Use line breaks between venues
           - Total response: under 250 words
           - No bullet points unless listing 3+ items

        OUTPUT: Plain text only. No JSON, no markdown, no code blocks.
        """;

    private const string ProactivePrompt = """
        You are The Meridian Agent — a highly personalized, empathetic, and discreet travel concierge. The traveler has NOT asked anything. You noticed a good moment on your own and are speaking first, briefly, in the middle of a conversation they can still reply to.

        YOUR PERSONALITY:
        - Warm, like a knowledgeable local friend
        - Concise — one short message, never more than 120 words
        - Discreet — you give heads-ups, not lectures
        - Confident — you make recommendations, not suggestions
        - You NEVER use phrases like "As an AI", "I recommend that you", "You might want to consider"

        <traveler>
        Name: $TravelerName
        Destination: $Destination
        Current location: $CurrentLocation
        Local time: $LocalTime
        Weather: $WeatherSummary
        </traveler>

        <your_observations>
        $SuggestionsJson
        </your_observations>

        RESPONSE RULES:
        1. Present the top-ranked observation(s) in plain text — at most 3, and only those that genuinely fit this exact moment.
        2. For each: one short line naming it, one short line saying why it fits them right now (use their preferences when relevant).
        3. Speak as if you just noticed it yourself ("It's cooling down — X is about ten minutes away"), not like a list of search results.
        4. End with a light invitation, not a question, and never "Is there anything else I can help you with?"
        5. No JSON, no markdown, no bullet points, no greetings.

        OUTPUT: Plain text only. No JSON, no markdown, no code blocks.
        """;

    private const string QuickActionsPrompt = """
        You are The Meridian Agent — a highly personalized, empathetic, and discreet travel concierge. You are the ONLY voice the user hears.

        YOUR PERSONALITY:
        - Warm, like a knowledgeable local friend
        - Concise — never ramble
        - Discreet — you give heads-up, not lectures
        - Confident — you make recommendations, not suggestions
        - You NEVER use phrases like "As an AI", "I recommend that you", "You might want to consider"

        <original_user_query>
        $UserQuery
        </original_user_query>

        <final_plan>
        $SearchAgentOutput
        </final_plan>

        <safeguard_report>
        $SafeguardAgentOutput
        </safeguard_report>

        MESSAGE RULES:
        1. OPENING (1 sentence): acknowledge the user's current state briefly. Never start with "Sure!" or "Of course!".
        2. THE PLAN (main body): present each venue with WHY it fits them specifically, using their preferences, travel time and suggested duration. Rank options ("Top pick:" / "Alternative:"). Separate venues with \n.
        3. HANDLING COMPROMISES: when a venue has a "compromise" value, never apologize and never hide it — present the trade and why the venue is still worth it ("The detour is worth it"). Mention the compromise only for the top-ranked venue. "compromise": null means the match was exact, so say nothing.
        4. SAFEGUARD WARNINGS: only if status = "FLAGGED". HIGH: "Important heads-up: [issue]. To keep things smooth, [fix]." MEDIUM: "Quick local note: [issue]. Easy fix: [fix]." LOW: brief only. Never use "illegal", "forbidden", "you must". If CLEARED, do not mention it at all.
        5. CLOSING (1 short sentence): offer the next action. Never end with "Is there anything else I can help you with?".
        6. FORMATTING: short paragraphs (2-3 sentences), under 250 words, no bullet points unless listing 3+ items, no markdown.

        OUTPUT: A single JSON object and nothing else — no markdown fences, no commentary:
        {
          "message": "the concierge reply, exactly as the rules above describe, with \n between venues",
          "quick_actions": [
            {"label": "...", "payload": "...", "action_type": "..."}
          ]
        }

        QUICK ACTION RULES:
        - ALWAYS include 2 to 3 quick actions whenever the message proposes at least one venue or next step. Only use "quick_actions": [] when there is genuinely nothing concrete to offer.
        - "label": 2 to 4 words, starting with exactly one emoji, no trailing punctuation.
        - "payload": what tapping it means, phrased as a natural request the traveler could have typed (for example "Plan an evening around Karak House").
        - "action_type": exactly one of "navigate" (show it on the map), "accept_suggestion" (plan this), "refine_search" (find alternatives).
        - Never use the same action_type twice, and point every payload at something from the plan above. Never invent venues.
        - When the top venue carries a "compromise", make one action a refine_search offering to look for something closer or better matched — the traveler should not have to type that themselves.
        """;

    private const string WelcomePrompt = """
        You are The Meridian Agent — a highly personalized, empathetic, and discreet travel concierge. The traveler has just finished onboarding, and you are speaking for the very first time. They have told you a lot about themselves, and this first message proves it.

        YOUR PERSONALITY:
        - Warm, like a knowledgeable local friend
        - Concise — never ramble
        - Discreet — you give heads-ups, not lectures
        - You NEVER use phrases like "As an AI", "I recommend that you", "You might want to consider"

        <traveler>
        Name: $TravelerName
        Destination: $Destination
        Travel style: $TravelStyle
        Energy pattern: $EnergyPattern
        Traveling as: $TravelGroup
        First-time visitor: $FirstTimeVisitor
        Dietary restrictions: $DietaryRestrictions
        Spice tolerance: $SpiceTolerance
        Favorite cuisines: $FavoriteCuisines
        Dining vibe: $DiningVibe
        Core interests: $CoreInterests
        Hard avoids: $HardAvoids
        Budget comfort: $BudgetComfort
        Mobility: $Mobility
        </traveler>

        RESPONSE RULES:
        1. OPENING: Greet them by their first name — one warm sentence that makes them feel recognized, not processed.
        2. PROOF YOU LISTENED: Weave in two or three of their actual preferences (travel style plus interests, dietary rules, or things they want to avoid). Be specific: say what they chose, not "your preferences".
        3. THE OFFER: Tell them you will quietly work around what they want and what they would rather skip — then invite them to ask for anything, in their own words.
        4. FORMATTING: 2-3 short sentences. No JSON, no markdown, no bullet points, no sign-off like "Is there anything else I can help you with?".

        OUTPUT: Plain text only.
        """;

    public override string AgentName => "SynthesizerAgent";

    public override string SanitizerModel { get; } = DefaultModel;

    public IAsyncEnumerable<string> StreamAsync(
        SynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        var prompt = BuildPrompt(request);
        return Client.StreamAsync(
            new LlmRequest(Model, prompt, JsonMode: false, Temperature: 0.7),
            cancellationToken);
    }

    public async Task<string> CompleteAsync(
        SynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder();
        await foreach (var token in StreamAsync(request, cancellationToken).ConfigureAwait(false))
        {
            builder.Append(token);
        }

        return builder.ToString();
    }

    public async Task<string> ExecuteProactiveAsync(
        IReadOnlyList<ProactiveSuggestion> suggestions,
        ProactiveRequest request,
        CancellationToken cancellationToken = default)
    {
        var prompt = PromptRenderer.Render(
            ProactivePrompt,
            new Dictionary<string, string>
            {
                ["TravelerName"] = request.Profile.DisplayName,
                ["Destination"] = request.Profile.Destination,
                ["CurrentLocation"] = request.Context?.Location ?? request.Profile.Destination,
                ["LocalTime"] = request.Context?.LocalTime ?? "unknown",
                ["WeatherSummary"] = request.Context?.WeatherSummary ?? "unknown",
                ["SuggestionsJson"] = JsonElementReader.Serialize(suggestions),
            });

        var builder = new StringBuilder();
        await foreach (var token in Client
            .StreamAsync(
                new LlmRequest(Model, prompt, JsonMode: false, Temperature: 0.7),
                cancellationToken)
            .ConfigureAwait(false))
        {
            builder.Append(token);
        }

        var message = builder.ToString().Trim();
        Logger.LogInformation(
            "Synthesized proactive message ({WordCount} words) from {SuggestionCount} suggestion(s)",
            message.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            suggestions.Count);

        return message;
    }

    /// <summary>Speaks the first message in a brand-new chat, right after onboarding.</summary>
    public async Task<string> ExecuteWelcomeAsync(
        UserProfile profile,
        CancellationToken cancellationToken = default)
    {
        var interests = profile.CoreInterests.Count > 0 ? profile.CoreInterests : profile.Interests;

        var prompt = PromptRenderer.Render(
            WelcomePrompt,
            new Dictionary<string, string>
            {
                ["TravelerName"] = profile.DisplayName,
                ["Destination"] = string.IsNullOrWhiteSpace(profile.Destination) ? "their destination" : profile.Destination,
                ["TravelStyle"] = profile.TravelStyle,
                ["EnergyPattern"] = profile.EnergyPattern,
                ["TravelGroup"] = profile.TravelGroup,
                ["FirstTimeVisitor"] = profile.FirstTimeVisitor ? "yes" : "no",
                ["DietaryRestrictions"] = List(profile.DietaryRestrictions.Count > 0 ? profile.DietaryRestrictions : profile.DietaryRules),
                ["SpiceTolerance"] = new TravelerOnboardingDraft { SpiceTolerance = profile.SpiceTolerance }.SpiceLabel,
                ["FavoriteCuisines"] = List(profile.FavoriteCuisines),
                ["DiningVibe"] = List(profile.DiningVibe),
                ["CoreInterests"] = List(interests),
                ["HardAvoids"] = List(profile.HardAvoids.Count > 0 ? profile.HardAvoids : profile.AvoidList),
                ["BudgetComfort"] = profile.BudgetComfort,
                ["Mobility"] = profile.Mobility,
            });

        var builder = new StringBuilder();
        await foreach (var token in Client
            .StreamAsync(
                new LlmRequest(Model, prompt, JsonMode: false, Temperature: 0.7),
                cancellationToken)
            .ConfigureAwait(false))
        {
            builder.Append(token);
        }

        var message = builder.ToString().Trim();
        Logger.LogInformation(
            "Synthesized welcome message ({WordCount} words) for {TravelerName}",
            message.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            profile.DisplayName);

        return message;

        static string List(IReadOnlyList<string> values) =>
            values.Count == 0 ? "none shared" : string.Join(", ", values);
    }

    private static string BuildPrompt(SynthesisRequest request)
    {
        var itineraryJson = JsonSerializer.Serialize(
            request.Itinerary.Venues,
            JsonDefaults.Options);

        var reportJson = JsonSerializer.Serialize(
            new
            {
                status = request.Report.IsFlagged ? "FLAGGED" : "CLEARED",
                warnings = request.Report.Warnings,
            },
            JsonDefaults.Options);

        return PromptRenderer.Render(
            Prompt,
            new Dictionary<string, string>
            {
                ["UserQuery"] = request.Query,
                ["SearchAgentOutput"] = itineraryJson,
                ["SafeguardAgentOutput"] = reportJson,
            });
    }

    /// <summary>
    /// Produces the reply and its quick actions in one JSON-mode call. Small models often break the
    /// envelope (literal newlines inside strings, prose around the JSON, a plain-text reply instead
    /// of JSON), so the reply is recovered through a chain of repairs and the worst case is a
    /// normal-looking message with no actions rather than an error.
    /// </summary>
    public async Task<SynthesisResponse> SynthesizeAsync(
        SynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        var prompt = PromptRenderer.Render(
            QuickActionsPrompt,
            new Dictionary<string, string>
            {
                ["UserQuery"] = request.Query,
                ["SearchAgentOutput"] = JsonElementReader.Serialize(request.Itinerary.Venues),
                ["SafeguardAgentOutput"] = JsonElementReader.Serialize(
                    new
                    {
                        status = request.Report.IsFlagged ? "FLAGGED" : "CLEARED",
                        warnings = request.Report.Warnings,
                    }),
            });

        var builder = new StringBuilder();
        await foreach (var token in Client
            .StreamAsync(
                new LlmRequest(Model, prompt, JsonMode: true, Temperature: 0.7),
                cancellationToken)
            .ConfigureAwait(false))
        {
            builder.Append(token);
        }

        var raw = builder.ToString().Trim();
        var response = ParseSynthesis(raw);

        Logger.LogInformation(
            "Synthesized structured response ({WordCount} words) with {ActionCount} quick action(s) for \"{Query}\"",
            response.Message.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            response.QuickActions.Count,
            request.Query);

        return response;
    }

    /// <summary>Recovers the message and quick actions from whatever the model actually returned.</summary>
    internal static SynthesisResponse ParseSynthesis(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new SynthesisResponse(
                "I couldn't put an answer together just now. Give me another try in a moment.",
                Array.Empty<QuickAction>());
        }

        // 1. Well-formed JSON envelope (the normal path).
        if (JsonExtractor.TryExtract(raw, out var document))
        {
            using (document)
            {
                var root = document!.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    var message = ReadMessage(root) ?? string.Empty;
                    return new SynthesisResponse(
                        string.IsNullOrWhiteSpace(message) ? FallbackText(raw) : message,
                        ReadQuickActions(root));
                }
            }
        }

        // 2. The envelope is almost right except for literal newlines inside its strings.
        var relaxed = RelaxEmbeddedNewlines(raw);
        if (JsonExtractor.TryExtract(relaxed, out var repaired))
        {
            using (repaired)
            {
                var root = repaired!.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    var message = ReadMessage(root) ?? string.Empty;
                    return new SynthesisResponse(
                        string.IsNullOrWhiteSpace(message) ? FallbackText(raw) : message,
                        ReadQuickActions(root));
                }
            }
        }

        // 3. The envelope is too broken to parse but the message is still readable.
        var recoveredMessage = RecoverMessageFromLooseJson(raw);

        return string.IsNullOrWhiteSpace(recoveredMessage)
            ? new SynthesisResponse(FallbackText(raw), Array.Empty<QuickAction>())
            : new SynthesisResponse(recoveredMessage, Array.Empty<QuickAction>());
    }

    private static string FallbackText(string raw)
    {
        var trimmed = raw.Trim();

        // A plain-text reply (the model ignored the JSON instruction) is the message.
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(trimmed)
                ? "I couldn't put an answer together just now. Give me another try in a moment."
                : trimmed;
        }

        return RecoverMessageFromLooseJson(trimmed)
            ?? "I couldn't put an answer together just now. Give me another try in a moment.";
    }

    /// <summary>Escapes literal newlines and tabs that sit inside JSON strings.</summary>
    internal static string RelaxEmbeddedNewlines(string raw)
    {
        var builder = new StringBuilder(raw.Length + 16);
        var inString = false;
        var escaped = false;

        foreach (var character in raw)
        {
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    builder.Append(character);
                    continue;
                }

                switch (character)
                {
                    case '\\':
                        escaped = true;
                        builder.Append(character);
                        continue;
                    case '"':
                        inString = false;
                        builder.Append(character);
                        continue;
                    case '\r':
                        builder.Append("\\r");
                        continue;
                    case '\n':
                        builder.Append("\\n");
                        continue;
                    case '\t':
                        builder.Append("\\t");
                        continue;
                    default:
                        builder.Append(character);
                        continue;
                }
            }

            if (character == '"')
            {
                inString = true;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>Last resort: pull the message string out of a broken envelope.</summary>
    private static string? RecoverMessageFromLooseJson(string raw)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            raw,
            "\"message\"\\s*:\\s*\"(.*?)\"\\s*[,}]",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        if (!match.Success)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<string>($"\"{match.Groups[1].Value}\"");
        }
        catch (JsonException)
        {
            return match.Groups[1].Value;
        }
    }

    private static string? ReadMessage(JsonElement root) =>
        root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
            ? message.GetString()
            : null;

    private static IReadOnlyList<QuickAction> ReadQuickActions(JsonElement root)
    {
        var actions = new List<QuickAction>();

        if (!root.TryGetProperty("quick_actions", out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return actions;
        }

        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var label = Read(item, "label");

            // A chip without a label is useless, and the label doubles as its payload fallback.
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            var payload = Read(item, "payload");
            actions.Add(new QuickAction(
                label.Trim(),
                (string.IsNullOrWhiteSpace(payload) ? label : payload).Trim(),
                QuickActionTypeExtensions.FromModelString(Read(item, "action_type"))));
        }

        // Never flood the chat: three chips is already a lot.
        return actions.Count > 3 ? actions.Take(3).ToList() : actions;
    }

    private static string? Read(JsonElement item, string name) =>
        item.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
