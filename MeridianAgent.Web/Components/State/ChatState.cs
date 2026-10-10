namespace MeridianAgent.Web.Components.State;

using MeridianAgent.Domain.Entities;

/// <summary>Chat transcript for the current circuit.</summary>
public sealed class ChatState
{
    private readonly List<ChatMessage> _messages = new();

    public IReadOnlyList<ChatMessage> Messages => _messages;

    public bool IsBusy { get; private set; }

    public string? StreamingContent { get; private set; }

    public event Action? Changed;

    public void AddUser(string content)
    {
        _messages.Add(new ChatMessage(Guid.NewGuid().ToString(), "user", content, DateTime.Now));
        NotifyChanged();
    }

    public void BeginAssistant()
    {
        StreamingContent = string.Empty;
        IsBusy = true;
        NotifyChanged();
    }

    public void AppendAssistant(string token)
    {
        StreamingContent += token;
        NotifyChanged();
    }

    public void EndAssistant()
    {
        if (StreamingContent is not null)
        {
            _messages.Add(new ChatMessage(
                Guid.NewGuid().ToString(),
                "assistant",
                StreamingContent,
                DateTime.Now,
                MessageType.Reactive));
        }

        StreamingContent = null;
        IsBusy = false;
        NotifyChanged();
    }

    /// <summary>Adds a finished assistant message (the post-onboarding welcome, for instance).</summary>
    public void AddAssistant(string content, MessageType type = MessageType.Reactive)
    {
        _messages.Add(new ChatMessage(
            Guid.NewGuid().ToString(),
            "assistant",
            content,
            DateTime.Now,
            type));
        NotifyChanged();
    }

    /// <summary>Adds an agent-initiated message that arrived over the chat hub while nobody asked.</summary>
    public void AddProactive(string messageId, string content)
    {
        _messages.Add(new ChatMessage(
            messageId,
            "assistant",
            content,
            DateTime.Now,
            MessageType.Proactive));
        NotifyChanged();
    }

    public void Reset()
    {
        _messages.Clear();
        StreamingContent = null;
        IsBusy = false;
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke();
}

/// <summary>Shared venue locations so Chat can show the map and the Map page can follow along.</summary>
public sealed class MapState
{
    private readonly List<Venue> _venues = new();
    private readonly List<NearbyVenue> _landmarks = new();

    public IReadOnlyList<Venue> Venues => _venues;

    public IReadOnlyList<NearbyVenue> Landmarks => _landmarks;

    public string CenterLabel { get; private set; } = string.Empty;

    public event Action? Changed;

    public void SetVenues(IReadOnlyList<Venue> venues)
    {
        _venues.Clear();
        _venues.AddRange(venues);
        NotifyChanged();
    }

    public void SetLandmarks(IReadOnlyList<NearbyVenue> landmarks, string centerLabel)
    {
        _landmarks.Clear();
        _landmarks.AddRange(landmarks);
        CenterLabel = centerLabel;
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke();
}
