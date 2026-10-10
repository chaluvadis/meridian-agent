namespace MeridianAgent.Web.Components.State;

/// <summary>Chat transcript for the current circuit.</summary>
public sealed class ChatState
{
    public sealed record ChatMessage(string Role, string Content);

    private readonly List<ChatMessage> _messages = new();

    public IReadOnlyList<ChatMessage> Messages => _messages;

    public bool IsBusy { get; private set; }

    public string? StreamingContent { get; private set; }

    public event Action? Changed;

    public void AddUser(string content)
    {
        _messages.Add(new ChatMessage("user", content));
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
            _messages.Add(new ChatMessage("assistant", StreamingContent));
        }

        StreamingContent = null;
        IsBusy = false;
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
