namespace MeridianAgent.Domain.Entities;

/// <summary>A single line in the chat transcript.</summary>
public sealed record ChatMessage(
    string Id,
    string Role,
    string Content,
    DateTime Timestamp,
    MessageType Type = MessageType.Reactive);

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
