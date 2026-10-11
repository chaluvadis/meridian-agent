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

    /// <summary>
    /// Produces the chat message plus the one-tap quick actions that belong beneath it, in a single
    /// JSON response. When the model fails to produce usable JSON the whole reply is returned as
    /// the message with no quick actions, so the chat degrades instead of breaking.
    /// </summary>
    Task<SynthesisResponse> SynthesizeAsync(SynthesisRequest request, CancellationToken cancellationToken = default);

    /// <summary>Formats proactive suggestions into one warm, chat-ready message (no user query exists).</summary>
    Task<string> ExecuteProactiveAsync(
        IReadOnlyList<ProactiveSuggestion> suggestions,
        ProactiveRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Speaks the first message in a fresh chat: greets the traveler by name and shows the
    /// preferences the agent now knows about. Used once, right after onboarding.
    /// </summary>
    Task<string> ExecuteWelcomeAsync(UserProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compiles the messages a traveler saved into one itinerary for the day. Empty when nothing is
    /// saved, because an itinerary is built from what was asked for, never invented.
    /// </summary>
    Task<string> GenerateItineraryAsync(
        UserProfile profile,
        IReadOnlyList<ChatMessage> savedMessages,
        CancellationToken cancellationToken = default);

    /// <summary>Tells the traveler the weather has turned against a plan they saved.</summary>
    Task<string> ExecuteWeatherPivotAsync(WeatherPivot pivot, CancellationToken cancellationToken = default);
}

/// <summary>
/// What the weather has done to a saved plan: the conditions, the outdoor stop it now threatens,
/// and the indoor places that may be offered instead. The available options are passed in rather
/// than looked up here, so the agent cannot invent a venue to rescue the plan.
/// </summary>
/// <param name="Condition">The weather as it reads to a person ("Thunderstorm").</param>
/// <param name="TemperatureC">Temperature in Celsius at the traveler's location.</param>
/// <param name="IsRaining">Whether rain is falling now.</param>
/// <param name="LocalTime">The hour at the destination, which is what the alert names.</param>
/// <param name="Location">Where the weather was read.</param>
/// <param name="SavedPlanSummary">What the traveler saved, in the words the alert refers back to.</param>
/// <param name="IndoorOptions">Sheltered places nearby, already known to fit their constraints.</param>
public sealed record WeatherPivot(
    string Condition,
    double TemperatureC,
    bool IsRaining,
    string LocalTime,
    string Location,
    string SavedPlanSummary,
    IReadOnlyList<string> IndoorOptions);

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
    /// <summary>The names the chat uses to tell one kind of agent-initiated message from another.</summary>
    public static class Kinds
    {
        /// <summary>An unprompted idea, offered because the traveler has been quiet.</summary>
        public const string Suggestion = "suggestion";

        /// <summary>A saved plan now conflicts with the weather.</summary>
        public const string WeatherPivot = "weatherPivot";
    }

    Task PublishAsync(string messageId, string content, CancellationToken cancellationToken = default);

    /// <summary>Publishes with an explicit <see cref="Kinds"/> tag.</summary>
    Task PublishAsync(
        string messageId,
        string content,
        string kind,
        CancellationToken cancellationToken = default);
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

/// <summary>The traveler id the app runs under: one profile, one onboarding state.</summary>
public interface IUserProfileService
{
    const string DefaultProfileId = "default";

    /// <summary>The stored profile, or null when the traveler has never completed onboarding.</summary>
    Task<UserProfile?> GetProfileAsync(string profileId = DefaultProfileId, CancellationToken cancellationToken = default);

    /// <summary>The stored profile, or <see cref="UserProfile.Default" /> when there is none yet.</summary>
    Task<UserProfile> GetOrCreateAsync(string profileId = DefaultProfileId, CancellationToken cancellationToken = default);

    /// <summary>True once onboarding has been completed; this is the first-time-user check.</summary>
    Task<bool> HasCompletedProfileAsync(string profileId = DefaultProfileId, CancellationToken cancellationToken = default);

    Task SaveProfileAsync(UserProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Clears the pending-welcome flag so the greeting is shown exactly once.</summary>
    Task MarkWelcomeSeenAsync(string profileId = DefaultProfileId, CancellationToken cancellationToken = default);
}

public interface IRulebookStore
{
    Task<DestinationRulebook?> GetAsync(string destination, CancellationToken cancellationToken = default);

    Task SaveAsync(DestinationRulebook rulebook, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DestinationRulebook>> ListAsync(CancellationToken cancellationToken = default);
}

public interface INearbyVenueStore
{
    /// <summary>
    /// Venues near a place. When <paramref name="at"/> is supplied it is used as the centre rather
    /// than the name, so an implementation with real venue data can answer for the actual position.
    /// </summary>
    Task<IReadOnlyList<NearbyVenue>> GetAsync(
        string location,
        GeoPoint? at = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A live source of the venues around a position, as opposed to a sample list keyed by place name.
/// </summary>
public interface ILiveVenueSource
{
    /// <summary>Whether this source has been asked to answer at all.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Venues really around a position, nearest first, or empty when there are none. Empty is a
    /// normal answer the caller falls back from, never an error.
    /// </summary>
    Task<IReadOnlyList<NearbyVenue>> GetAroundAsync(
        GeoPoint centre,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// How long it takes to get from one position to another, from a routing service rather than from a
/// number somebody wrote in a data file.
/// </summary>
public interface ITravelTimeProvider
{
    /// <summary>Whether this provider is configured and worth calling.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Driving minutes between two positions, or null when the service is unreachable, the route is
    /// impossible (across water, off-road), or either end has no coordinates.
    /// </summary>
    Task<int?> MinutesAsync(GeoPoint from, GeoPoint to, CancellationToken cancellationToken = default);
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
    /// <summary>
    /// The live reality at a place. When <paramref name="at"/> is supplied it is used as the
    /// position directly, so the weather describes where the traveler is standing rather than the
    /// centre of the city they typed.
    /// </summary>
    Task<TravelContext> GetCurrentAsync(
        string location,
        GeoPoint? at = null,
        CancellationToken cancellationToken = default);
}
