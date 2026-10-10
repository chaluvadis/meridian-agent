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

        3. SAFEGUARD WARNINGS (if any):
           - ONLY if status = "FLAGGED"
           - For HIGH severity: Be direct but calm. "Important heads-up: [issue]. To keep things smooth, [gentle_fix]."
           - For MEDIUM severity: Casual tone. "Quick local note: [issue]. Easy fix: [gentle_fix]."
           - For LOW severity: Brief mention only if relevant.
           - NEVER use words like: "illegal", "forbidden", "you must", "strictly prohibited", "offense"
           - INSTEAD use: "heads-up", "local note", "to keep things smooth", "just so you know"
           - If status = "CLEARED", DO NOT mention the safeguard agent at all.

        4. CLOSING (1 short sentence):
           - Offer the next action: "Want me to reroute your taxi there?" or "Should I adjust your evening plans around this?"
           - Never end with "Is there anything else I can help you with?"

        5. FORMATTING:
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
}
