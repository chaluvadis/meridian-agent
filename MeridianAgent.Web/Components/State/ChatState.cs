namespace MeridianAgent.Web.Components.State;

using MeridianAgent.Domain.Entities;

/// <summary>Chat transcript for the current circuit.</summary>
public sealed class ChatState
{
    private readonly List<ChatMessage> _messages = new();

    public IReadOnlyList<ChatMessage> Messages => _messages;

    public bool IsBusy { get; private set; }

    public string? StreamingContent { get; private set; }

    /// <summary>The calendar day the transcript currently on screen belongs to.</summary>
    public DateOnly SessionDate { get; private set; } = DateOnly.FromDateTime(DateTime.Now);

    /// <summary>True while the transcript is a past day rather than today.</summary>
    public bool IsViewingHistory { get; private set; }

    /// <summary>Raised when a bookmark is added or removed, so the Glance panel can refresh live.</summary>
    public event Action? FavoritesChanged;

    public event Action? Changed;

    public void AddUser(string content)
    {
        _messages.Add(new ChatMessage(Guid.NewGuid().ToString(), "user", content, DateTime.Now));
        NotifyChanged();
    }

    /// <summary>Marks a message as stored, so the favourite toggle can find it after a reload.</summary>
    public void AttachStoredId(string messageId, int storedId)
    {
        var message = _messages.FirstOrDefault(entry => entry.Id == messageId);

        if (message is null)
        {
            return;
        }

        var index = _messages.IndexOf(message);
        _messages[index] = message with { StoredId = storedId };
    }

    /// <summary>Applies the state a bookmark was just set to, without waiting for a re-read.</summary>
    public void SetFavorite(string messageId, bool isFavorite)
    {
        var message = _messages.FirstOrDefault(entry => entry.Id == messageId);

        if (message is null)
        {
            return;
        }

        var index = _messages.IndexOf(message);
        _messages[index] = message with { IsFavorite = isFavorite };
        NotifyChanged();
        FavoritesChanged?.Invoke();
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

    /// <summary>
    /// Commits the streamed reply, attaching the one-tap quick actions that belong under it.
    /// </summary>
    public void EndAssistant(IReadOnlyList<QuickAction>? quickActions = null)
    {
        if (StreamingContent is not null)
        {
            _messages.Add(new ChatMessage(
                Guid.NewGuid().ToString(),
                "assistant",
                StreamingContent,
                DateTime.Now,
                MessageType.Reactive,
                quickActions?.ToList()));
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
    public void AddProactive(string messageId, string content, MessageType type = MessageType.Proactive)
    {
        _messages.Add(new ChatMessage(
            messageId,
            "assistant",
            content,
            DateTime.Now,
            type));
        NotifyChanged();
    }

    public void Reset()
    {
        _messages.Clear();
        StreamingContent = null;
        IsBusy = false;
        IsViewingHistory = false;
        SessionDate = DateOnly.FromDateTime(DateTime.Now);
        NotifyChanged();
    }

    /// <summary>
    /// Replaces the transcript with a stored day. The messages keep their ids and bookmark state,
    /// so hearts and the Glance panel continue to refer to the same rows.
    /// </summary>
    public void LoadSession(DateOnly date, IReadOnlyList<ChatMessage> messages, bool isHistory)
    {
        _messages.Clear();
        _messages.AddRange(messages);
        StreamingContent = null;
        IsBusy = false;
        SessionDate = date;
        IsViewingHistory = isHistory;
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke();
}

/// <summary>Shared venue locations so Chat can show the map sheet and follow along.</summary>
public sealed class MapState
{
    private readonly List<Venue> _venues = new();
    private readonly List<NearbyVenue> _landmarks = new();

    public IReadOnlyList<Venue> Venues => _venues;

    public IReadOnlyList<NearbyVenue> Landmarks => _landmarks;

    public string CenterLabel { get; private set; } = string.Empty;

    /// <summary>Place the map should pan to next; cleared once it has been applied.</summary>
    public string? FocusLabel { get; private set; }

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

    /// <summary>Asks the map to pan to a named place (used by "Show on Map" quick actions).</summary>
    public void SetFocus(string? label)
    {
        FocusLabel = string.IsNullOrWhiteSpace(label) ? null : label;
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke();
}
