namespace MeridianAgent.Infrastructure.Scheduling;

/// <summary>How often and when proactive suggestions are pushed into the chat.</summary>
public sealed class ProactiveSuggestionsOptions
{
    public const string SectionName = "Proactive";

    /// <summary>Master switch (env: Proactive__Enabled).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hours between proactive runs (env: Proactive__IntervalHours).</summary>
    public double IntervalHours { get; set; } = 4;

    /// <summary>Only surface suggestions whose confidence score is at least this (env: Proactive__MinConfidenceScore).</summary>
    public double MinConfidenceScore { get; set; } = 0.55;

    /// <summary>Do not repeat the same suggestion(s) within this many hours (env: Proactive__CooldownHours).</summary>
    public double CooldownHours { get; set; } = 2;

    /// <summary>Only speak when the traveler used the chat within this many minutes (env: Proactive__ActiveWithinMinutes).</summary>
    public int ActiveWithinMinutes { get; set; } = 30;
}
