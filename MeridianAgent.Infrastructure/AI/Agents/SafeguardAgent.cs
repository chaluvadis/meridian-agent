namespace MeridianAgent.Infrastructure.AI.Agents;

/// <summary>
/// Agent 3: the quiet guardian. Checks the proposed plan against the destination rulebook and
/// flags risks with gentle fixes. Model: qwen2.5:3b.
/// </summary>
public sealed class SafeguardAgent(
    ILlmClient client,
    ILogger<SafeguardAgent> logger,
    string? model = null,
    string? sanitizerModel = null) : AgentBase(client, logger, model ?? DefaultModel), ISafeguardAgent
{
    public const string DefaultModel = "qwen2.5:3b";

    private const string Prompt = """
        You are a cultural compliance agent for $Destination. Your job is to check whether the proposed plan contains any risk of violating local laws, cultural norms, or venue-specific rules.

        You are a QUIET GUARDIAN. You flag risks so the synthesizer can give the user a friendly heads-up. You do NOT lecture, moralize, or refuse plans. You simply identify risks.

        <proposed_plan>
        $SearchAgentOutput
        </proposed_plan>

        <user_context>
        {
          "traveler_profile": "$FirstTimeOrExperienced",
          "current_attire_noted": "$UserAttireOrUnknown",
          "group_composition": "$GroupComposition"
        }
        </user_context>

        <destination_rulebook>
        $DestinationRulebookJson
        </destination_rulebook>

        STRICT RULES:
        1. Output ONLY valid JSON. No markdown, no explanations.
        2. Check EACH venue in the proposed plan against the rulebook.
        3. Classify each risk by severity:
           - "high" = legal risk, fines, or device confiscation
           - "medium" = will cause denied entry or social friction
           - "low" = polite consideration, not enforced
        4. For each flagged risk, provide a "gentle_fix" — a practical, non-judgmental way to avoid the issue.
        5. If the plan is fully compliant, return status "CLEARED" with empty warnings array.
        6. NEVER suggest changing the user's plans. Only flag risks and offer fixes.

        OUTPUT FORMAT (exact):
        {
          "status": "CLEARED | FLAGGED",
          "warnings": [
            {
              "applies_to_venue": "venue_name from proposed plan",
              "severity": "high | medium | low",
              "rule_category": "dress_code | photography | behavior | alcohol | pda | other",
              "risk_description": "One sentence describing the specific risk.",
              "gentle_fix": "One practical, friendly suggestion to avoid the issue."
            }
          ]
        }
        """;

    public override string AgentName => "SafeguardAgent";

    public override string SanitizerModel { get; } = sanitizerModel ?? DefaultModel;

    public Task<SafeguardReport> CheckAsync(
        ProposedItinerary plan,
        TravelRequest request,
        CancellationToken cancellationToken = default)
    {
        var profile = request.Profile;
        var destination = request.Rulebook?.Destination;
        if (string.IsNullOrWhiteSpace(destination))
        {
            destination = profile.Destination;
        }

        var prompt = PromptRenderer.Render(
            Prompt,
            new Dictionary<string, string>
            {
                ["Destination"] = destination ?? "the current destination",
                ["SearchAgentOutput"] = JsonElementReader.Serialize(plan),
                ["FirstTimeOrExperienced"] = profile.TravelerProfile == TravelerProfile.FirstTime
                    ? "first-time visitor"
                    : "experienced traveler",
                ["UserAttireOrUnknown"] = string.IsNullOrWhiteSpace(profile.AttireNote) ? "unknown" : profile.AttireNote,
                ["GroupComposition"] = profile.GroupComposition switch
                {
                    GroupComposition.Solo => "Solo",
                    GroupComposition.Couple => "Couple",
                    GroupComposition.Family => "Family",
                    GroupComposition.Friends => "Friends",
                    _ => profile.GroupComposition.ToString(),
                },
                ["DestinationRulebookJson"] = JsonElementReader.Serialize(request.Rulebook),
            });

        return RunJsonAsync(prompt, ParseReport, cancellationToken);
    }

    private static ParseOutcome<SafeguardReport> ParseReport(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return ParseOutcome<SafeguardReport>.Fail("expected a JSON object");
        }

        var errors = new List<string>();
        var status = JsonElementReader.ReadEnum<SafeguardStatus>(root, "status", errors, required: true);
        if (status is null)
        {
            return ParseOutcome<SafeguardReport>.Fail(errors.ToArray());
        }

        var warnings = new List<RiskWarning>();
        if (root.TryGetProperty("warnings", out var warningsElement) &&
            warningsElement.ValueKind != JsonValueKind.Null)
        {
            if (warningsElement.ValueKind != JsonValueKind.Array)
            {
                return ParseOutcome<SafeguardReport>.Fail("field 'warnings' must be an array");
            }

            foreach (var item in warningsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    errors.Add("each warning must be a JSON object");
                    continue;
                }

                var appliesTo = JsonElementReader.ReadString(item, "applies_to_venue", errors, required: false);
                var severity = JsonElementReader.ReadEnum<RiskSeverity>(item, "severity", errors, required: false) ?? RiskSeverity.Low;
                var category = JsonElementReader.ReadEnum<RuleCategory>(item, "rule_category", errors, required: false) ?? RuleCategory.Other;

                warnings.Add(new RiskWarning
                {
                    AppliesToVenue = appliesTo ?? string.Empty,
                    Severity = severity,
                    RuleCategory = category,
                    RiskDescription = JsonElementReader.ReadString(item, "risk_description", errors, required: false) ?? string.Empty,
                    GentleFix = JsonElementReader.ReadString(item, "gentle_fix", errors, required: false) ?? string.Empty,
                });
            }
        }

        if (errors.Count > 0)
        {
            return ParseOutcome<SafeguardReport>.Fail(errors.ToArray());
        }

        var effectiveStatus = warnings.Count == 0 ? SafeguardStatus.Cleared : status.Value;
        return ParseOutcome<SafeguardReport>.Ok(new SafeguardReport(effectiveStatus, warnings));
    }
}
