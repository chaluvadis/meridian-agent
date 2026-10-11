namespace MeridianAgent.Web.Hubs;

/// <summary>
/// SignalR hub that carries agent-initiated messages into the chat. The scheduler never calls this
/// hub directly — it publishes through <see cref="IProactiveChatNotifier" /> implemented on top of
/// the hub context, so the Web layer can change transports without touching the pipeline.
/// </summary>
public sealed class ChatHub(IChatActivityTracker activity) : Hub
{
    public override Task OnConnectedAsync()
    {
        activity.RecordActivity();
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        activity.RecordActivity();
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Called by the chat client on user interaction so proactive timing follows real sessions.</summary>
    public Task Ping()
    {
        activity.RecordActivity();
        return Task.CompletedTask;
    }
}
