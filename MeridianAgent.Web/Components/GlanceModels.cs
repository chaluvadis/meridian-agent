namespace MeridianAgent.Web.Components;

using MeridianAgent.Domain.Entities;

/// <summary>One bookmark, reduced to what the Saved tab needs to render it.</summary>
public sealed record FavoriteRow(
    int MessageId,
    DateTime Timestamp,
    string DayLabel,
    string Stamp,
    string Preview,
    string Content,
    RichContent? RichContent);

/// <summary>One day of conversation, as the Timeline tab lists it.</summary>
public sealed record DayRow(
    int SessionId,
    DateOnly Date,
    int DayNumber,
    int Count,
    string Label,
    bool IsToday);
