namespace MeridianAgent.Web.Hubs;

using MeridianAgent.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

/// <summary>Bridges the scheduler's notifier to connected chat clients over SignalR.</summary>
public sealed class HubChatNotifier(IHubContext<ChatHub> hubContext, ILogger<HubChatNotifier> logger) : IProactiveChatNotifier
{
    public async Task PublishAsync(
        string messageId,
        string content,
        CancellationToken cancellationToken = default)
    {
        await PublishAsync(messageId, content, IProactiveChatNotifier.Kinds.Suggestion, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Publishes agent-initiated text with what kind it is, so the chat can show a weather alarm
    /// differently from a gentle suggestion.
    /// </summary>
    public async Task PublishAsync(
        string messageId,
        string content,
        string kind,
        CancellationToken cancellationToken = default)
    {
        await hubContext.Clients.All
            .SendAsync("ReceiveProactiveMessage", messageId, content, kind, cancellationToken)
            .ConfigureAwait(false);

        logger.LogDebug(
            "Broadcast {Kind} message {MessageId} to all chat clients",
            kind,
            messageId);
    }
}
