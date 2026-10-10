using MeridianAgent.Domain.AgentContracts;

namespace MeridianAgent.Application.State;

/// <summary>One value published to the blackboard, kept for debugging and observability.</summary>
public sealed record BlackboardEntry(string Key, object? Value, DateTimeOffset WrittenAtUtc);

/// <summary>
/// In-memory shared state the four agents write to and read from while a query is processed.
/// A fresh instance is used per pipeline run; it is thread-safe so agents can publish concurrently.
/// </summary>
public sealed class AgentBlackboard
{
    public static class Keys
    {
        public const string Query = "query";
        public const string Profile = "profile";
        public const string Context = "context";
        public const string Constraints = "extracted_constraints";
        public const string Itinerary = "proposed_itinerary";
        public const string SafeguardReport = "safeguard_report";
        public const string Response = "response";
    }

    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly List<BlackboardEntry> _history = new();
    private readonly object _gate = new();

    public void Set<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_gate)
        {
            _values[key] = value;
            _history.Add(new BlackboardEntry(key, value, DateTimeOffset.UtcNow));
        }
    }

    public T? Get<T>(string key)
    {
        lock (_gate)
        {
            return _values.TryGetValue(key, out var value) && value is T typed
                ? typed
                : default;
        }
    }

    public bool TryGet<T>(string key, out T? value)
    {
        value = Get<T>(key);
        return value is not null;
    }

    public IReadOnlyList<BlackboardEntry> History
    {
        get
        {
            lock (_gate)
            {
                return _history.ToArray();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _values.Clear();
            _history.Clear();
        }
    }

    public void PublishQuery(string query) => Set(Keys.Query, query);

    public string? Query => Get<string>(Keys.Query);

    public void PublishConstraints(ExtractedConstraints constraints) => Set(Keys.Constraints, constraints);

    public ExtractedConstraints? Constraints => Get<ExtractedConstraints>(Keys.Constraints);

    public void PublishItinerary(ProposedItinerary itinerary) => Set(Keys.Itinerary, itinerary);

    public ProposedItinerary? Itinerary => Get<ProposedItinerary>(Keys.Itinerary);

    public void PublishSafeguardReport(SafeguardReport report) => Set(Keys.SafeguardReport, report);

    public SafeguardReport? SafeguardReport => Get<SafeguardReport>(Keys.SafeguardReport);

    public void PublishResponse(string response) => Set(Keys.Response, response);

    public string? Response => Get<string>(Keys.Response);
}
