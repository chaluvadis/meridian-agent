namespace MeridianAgent.Application.Abstractions;

public interface IContextAgent
{
    string AgentName => "ContextAgent";

    Task<ExtractedConstraints> ExtractAsync(TravelRequest request, CancellationToken cancellationToken = default);
}

public interface ISearchAgent
{
    string AgentName => "SearchAgent";

    Task<ProposedItinerary> ProposeAsync(
        ExtractedConstraints constraints,
        TravelRequest request,
        CancellationToken cancellationToken = default);
}

public interface ISafeguardAgent
{
    string AgentName => "SafeguardAgent";

    Task<SafeguardReport> CheckAsync(
        ProposedItinerary plan,
        TravelRequest request,
        CancellationToken cancellationToken = default);
}

public interface ISynthesizerAgent
{
    string AgentName => "SynthesizerAgent";

    IAsyncEnumerable<string> StreamAsync(SynthesisRequest request, CancellationToken cancellationToken = default);

    Task<string> CompleteAsync(SynthesisRequest request, CancellationToken cancellationToken = default);

    /// <summary>Formats proactive suggestions into one warm, chat-ready message (no user query exists).</summary>
    Task<string> ExecuteProactiveAsync(
        IReadOnlyList<ProactiveSuggestion> suggestions,
        ProactiveRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Generates unprompted suggestions from the traveler's profile, context, and nearby venues.</summary>
public interface IProactiveSuggestionAgent
{
    string AgentName => "ProactiveSuggestionAgent";

    Task<IReadOnlyList<ProactiveSuggestion>> GenerateAsync(
        ProactiveRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Pushes agent-initiated messages to connected chat clients. Implemented in the Web layer so the
/// scheduling infrastructure never depends on SignalR.
/// </summary>
public interface IProactiveChatNotifier
{
    Task PublishAsync(string messageId, string content, CancellationToken cancellationToken = default);
}

/// <summary>Tracks recent traveler interaction so proactive messages only go to an engaged session.</summary>
public interface IChatActivityTracker
{
    void RecordActivity();

    DateTime LastActivityUtc { get; }

    bool IsActive(TimeSpan window);
}

public interface IUserProfileStore
{
    Task<UserProfile?> GetAsync(string profileId, CancellationToken cancellationToken = default);

    Task SaveAsync(UserProfile profile, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserProfile>> ListAsync(CancellationToken cancellationToken = default);
}

public interface IRulebookStore
{
    Task<DestinationRulebook?> GetAsync(string destination, CancellationToken cancellationToken = default);

    Task SaveAsync(DestinationRulebook rulebook, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DestinationRulebook>> ListAsync(CancellationToken cancellationToken = default);
}

public interface INearbyVenueStore
{
    Task<IReadOnlyList<NearbyVenue>> GetAsync(string location, CancellationToken cancellationToken = default);
}

/// <summary>
/// Narrows a rulebook to the rules that are relevant to a query (vector search over Qdrant).
/// Implementations must degrade gracefully: return the original rulebook when nothing matches
/// or the vector store is unreachable.
/// </summary>
public interface IRulebookNarrower
{
    Task<DestinationRulebook> NarrowAsync(
        DestinationRulebook rulebook,
        string query,
        CancellationToken cancellationToken = default);
}

/// <summary>Provides the live travel context (location, local time, weather) for the traveler.</summary>
public interface ITravelContextProvider
{
    Task<TravelContext> GetCurrentAsync(string location, CancellationToken cancellationToken = default);
}
