namespace MeridianAgent.Domain.Entities;

/// <summary>
/// An action the traveler can take with a single tap, rendered as a chip underneath an agent
/// message. The payload is what the app sends back to the orchestrator when the chip is tapped, so
/// acting on a suggestion never requires typing.
/// </summary>
/// <param name="Label">Chip text, emoji included, e.g. "📌 Show on Map".</param>
/// <param name="Payload">
/// What the tap means in words — for <see cref="QuickActionType.AcceptSuggestion" /> and
/// <see cref="QuickActionType.RefineSearch" /> this is the query sent back through the pipeline,
/// for <see cref="QuickActionType.Navigate" /> the venue or place to point at.
/// </param>
/// <param name="ActionType">What the tap should do.</param>
public sealed record QuickAction(string Label, string Payload, QuickActionType ActionType);

/// <summary>The three things a quick action chip can do.</summary>
public enum QuickActionType
{
    /// <summary>Show what the agent just mentioned — for example pin it on the map. No LLM call.</summary>
    Navigate,

    /// <summary>Accept the suggestion and let the agent plan it properly.</summary>
    AcceptSuggestion,

    /// <summary>Ask for alternatives to what the agent proposed.</summary>
    RefineSearch,
}

/// <summary>
/// Tolerant mapping from the action-type strings small models emit ("show_map", "refine",
/// "accept_suggestion", …) onto the canonical set. Anything unrecognised becomes
/// <see cref="QuickActionType.RefineSearch" /> so the chip still does something useful.
/// </summary>
public static class QuickActionTypeExtensions
{
    public static QuickActionType FromModelString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return QuickActionType.RefineSearch;
        }

        var normalized = new string(raw.ToLowerInvariant().Where(char.IsLetter).ToArray());

        if (ContainsAny(normalized, "navigate", "show", "map", "open", "locate", "pin", "view"))
        {
            return QuickActionType.Navigate;
        }

        if (ContainsAny(normalized, "accept", "plan", "save", "book", "take", "do"))
        {
            return QuickActionType.AcceptSuggestion;
        }

        return QuickActionType.RefineSearch;
    }

    /// <summary>Short, stable key for the action type (used in prompts and telemetry).</summary>
    public static string ToModelString(this QuickActionType type) => type switch
    {
        QuickActionType.Navigate => "navigate",
        QuickActionType.AcceptSuggestion => "accept_suggestion",
        _ => "refine_search",
    };

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));
}
