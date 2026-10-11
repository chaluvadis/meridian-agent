namespace MeridianAgent.Domain.Entities;

/// <summary>
/// A persisted chat message. Kept as its own class rather than reusing the
/// <see cref="ChatMessage" /> record because the two do different jobs: this one is the row in
/// SQLite (mutable, identity-driven, carries the JSON columns), the record is the transient shape
/// the chat renders and streams.
/// </summary>
public sealed class ChatMessageEntity
{
    public int Id { get; set; }

    /// <summary>The day this message belongs to.</summary>
    public int SessionId { get; set; }

    public ChatSession Session { get; set; } = null!;

    /// <summary>"user" or "assistant".</summary>
    public string Role { get; set; } = "user";

    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// When the message was written, in local time. The app renders "HH:mm" straight off it, and
    /// the day grouping uses its date part, so the two always agree.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>True when the traveler bookmarked this reply; drives the Glance panel's Saved tab.</summary>
    public bool IsFavorite { get; set; }

    /// <summary>Serialized structured blocks (see <see cref="RichContent" />), null when there are none.</summary>
    public string? RichContentJson { get; set; }

    /// <summary>Serialized one-tap chips, null for user messages and replies that proposed nothing.</summary>
    public string? QuickActionsJson { get; set; }
}
