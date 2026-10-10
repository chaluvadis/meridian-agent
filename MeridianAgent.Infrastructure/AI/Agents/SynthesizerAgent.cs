using System.Text;
using System.Text.Json;
using MeridianAgent.Application.Abstractions;
using MeridianAgent.Application.DTOs;
using Microsoft.Extensions.Logging;

namespace MeridianAgent.Infrastructure.AI.Agents;

/// <summary>
/// Agent 4: the only voice the user hears. Weaves the plan and any safeguard warnings into a warm,
/// concise concierge response. Model: llama3.1:8b. Output streams token-by-token to the Blazor UI.
/// </summary>
public sealed class SynthesizerAgent : AgentBase, ISynthesizerAgent
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

    public SynthesizerAgent(
        ILlmClient client,
        ILogger<SynthesizerAgent> logger,
        string? model = null)
        : base(client, logger, model ?? DefaultModel)
    {
        SanitizerModel = DefaultModel;
    }

    public override string AgentName => "SynthesizerAgent";

    public override string SanitizerModel { get; }

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
