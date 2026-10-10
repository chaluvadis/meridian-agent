namespace MeridianAgent.Domain.Entities;

/// <summary>A single line in the chat transcript.</summary>
/// <param name="QuickActions">
/// One-tap actions rendered under the message. Present on agent messages that propose something
/// concrete, null on everything else.
/// </param>
public sealed record ChatMessage(
    string Id,
    string Role,
    string Content,
    DateTime Timestamp,
    MessageType Type = MessageType.Reactive,
    List<QuickAction>? QuickActions = null);

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
