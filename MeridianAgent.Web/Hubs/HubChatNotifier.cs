namespace MeridianAgent.Web.Hubs;

using MeridianAgent.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

/// <summary>Bridges the scheduler's notifier to connected chat clients over SignalR.</summary>
public sealed class HubChatNotifier(IHubContext<ChatHub> hubContext, ILogger<HubChatNotifier> logger) : IProactiveChatNotifier
{
    public async Task PublishAsync(string messageId, string content, CancellationToken cancellationToken = default)
    {
        await hubContext.Clients.All
            .SendAsync("ReceiveProactiveMessage", messageId, content, cancellationToken)
            .ConfigureAwait(false);

        logger.LogDebug("Broadcast proactive message {MessageId} to all chat clients", messageId);
    }
}
