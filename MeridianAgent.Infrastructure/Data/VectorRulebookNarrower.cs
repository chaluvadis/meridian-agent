namespace MeridianAgent.Infrastructure.Data;

/// <summary>
/// Narrows a rulebook to the rules most relevant to the user's query using embeddings and Qdrant
/// vector search. Degrades to the full rulebook on any failure — including no embedding generator
/// being configured at all — so the safeguard stage never breaks.
/// </summary>
public sealed class VectorRulebookNarrower(
    IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator,
    QdrantClient qdrant,
    ILogger<VectorRulebookNarrower> logger) : IRulebookNarrower
{
    private readonly IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator = embeddingGenerator;
    private readonly QdrantClient _qdrant = qdrant;
    private readonly ILogger<VectorRulebookNarrower> _logger = logger;

    /// <summary>Embeds and indexes every rule of a destination so it can be found by similarity.</summary>
    public async Task EnsureIndexedAsync(DestinationRulebook rulebook, CancellationToken cancellationToken = default)
    {
        if (_embeddingGenerator is null)
        {
            // The Kilo Gateway serves chat models only, so this is the default deployment: the
            // safeguard reads the whole rulebook, which is a handful of rules per destination.
            _logger.LogInformation(
                "No embedding model configured, so {Destination} keeps its {Count} rule(s) unindexed and the safeguard reads the full rulebook",
                rulebook.Destination,
                rulebook.Rules.Count);
            return;
        }

        // Embed everything BEFORE touching the collection: a failure here — a rejected key, an empty
        // quota, an outage — must leave whichever collection exists, old size or not, exactly as it
        // was. The other order deletes the collection first, and a failed re-embed then silently
        // costs the destination its vector search until a restart happens to succeed.
        var points = new List<(string RuleId, float[] Vector, string RuleText)>(rulebook.Rules.Count);

        foreach (var rule in rulebook.Rules)
        {
            var vector = await EmbedAsync($"{rule.Category}: {rule.Rule}", cancellationToken).ConfigureAwait(false);
            points.Add((rule.RuleId, vector, rule.Rule));
        }

        var collection = CollectionFor(rulebook.Destination);
        await _qdrant.EnsureCollectionAsync(collection, cancellationToken).ConfigureAwait(false);

        foreach (var point in points)
        {
            await _qdrant.UpsertAsync(collection, point.RuleId, point.Vector, new Dictionary<string, string>
            {
                ["rule_id"] = point.RuleId,
                ["destination"] = rulebook.Destination,
                ["rule"] = point.RuleText,
            }, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Indexed {Count} rule(s) for {Destination} into Qdrant", rulebook.Rules.Count, rulebook.Destination);
    }

    public async Task<DestinationRulebook> NarrowAsync(
        DestinationRulebook rulebook,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (_embeddingGenerator is null || rulebook.Rules.Count <= 2)
        {
            return rulebook;
        }

        try
        {
            var vector = await EmbedAsync(query, cancellationToken).ConfigureAwait(false);

            var hits = await _qdrant
                .SearchAsync(CollectionFor(rulebook.Destination), vector, limit: 5, cancellationToken)
                .ConfigureAwait(false);

            if (hits.Count == 0)
            {
                return rulebook;
            }

            var matched = hits
                .Select(hit => hit.Payload.TryGetValue("rule_id", out var id) ? id : hit.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var relevant = rulebook.Rules.Where(rule => matched.Contains(rule.RuleId)).ToArray();
            if (relevant.Length == 0)
            {
                return rulebook;
            }

            _logger.LogInformation(
                "Narrowed {Destination} rulebook from {Before} to {After} rule(s) via vector search",
                rulebook.Destination,
                rulebook.Rules.Count,
                relevant.Length);

            return rulebook with { Rules = relevant };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector narrowing failed for {Destination}; using the full rulebook", rulebook.Destination);
            return rulebook;
        }
    }

    public static string CollectionFor(string destination, string prefix = "meridian") =>
        $"{prefix}-rules-{string.Join('-', (destination ?? "unknown").ToLowerInvariant().Split(Path.GetInvalidFileNameChars()))}";

    /// <summary>
    /// One piece of text as a vector. The generator owns the model and its dimension; this is the
    /// seam that used to know about a model name, and no longer needs to. Every caller has already
    /// established that a generator is configured.
    /// </summary>
    private async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var vector = await _embeddingGenerator!
            .GenerateVectorAsync(text, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return vector.ToArray();
    }
}
