namespace MeridianAgent.Domain.Entities;

/// <summary>A single line in the chat transcript.</summary>
/// <param name="QuickActions">
/// One-tap actions rendered under the message. Present on agent messages that propose something
/// concrete, null on everything else.
/// </param>
/// <param name="StoredId">
/// The id of the persisted row, once the message has been saved. Null while it only exists in the
/// circuit, which is what makes the favourite toggle able to find it again after a reload.
/// </param>
/// <param name="IsFavorite">True when the traveler bookmarked this reply.</param>
public sealed record ChatMessage(
    string Id,
    string Role,
    string Content,
    DateTime Timestamp,
    MessageType Type = MessageType.Reactive,
    List<QuickAction>? QuickActions = null,
    int? StoredId = null,
    bool IsFavorite = false);

/// <summary>
/// How a chat message came to exist. Reactive answers a user question; Proactive means the agent
/// spoke without being asked; System is for service notifications (for example a reconnect).
/// </summary>
public enum MessageType
{
    Reactive,
    Proactive,
    System,
}
