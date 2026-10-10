namespace MeridianAgent.Web.Components.State;

using MeridianAgent.Application.Abstractions;

/// <summary>
/// In-memory record of the traveler's last chat interaction. Proactive messages only go out while
/// the session is fresh; the timestamp is written with interlocked ticks so the scheduler (a
/// background thread) and the UI circuit stay race-free.
/// </summary>
public sealed class ChatActivityTracker : IChatActivityTracker
{
    // Zero until a traveler actually connects or interacts — a fresh app must not push proactively.
    private long _lastActivityTicks;

    public DateTime LastActivityUtc => new(Interlocked.Read(ref _lastActivityTicks));

    public void RecordActivity() => Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

    public bool IsActive(TimeSpan window)
    {
        var last = new DateTime(Interlocked.Read(ref _lastActivityTicks), DateTimeKind.Utc);
        return DateTime.UtcNow - last <= window;
    }
}
