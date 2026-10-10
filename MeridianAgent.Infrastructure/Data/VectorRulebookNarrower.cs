namespace MeridianAgent.Infrastructure.Data;

/// <summary>
/// Narrows a rulebook to the rules most relevant to the user's query using embeddings and Qdrant
/// vector search. Degrades to the full rulebook on any failure so the safeguard stage never breaks.
/// </summary>
public sealed class VectorRulebookNarrower(
    ILlmClient llmClient,
    QdrantClient qdrant,
    IOptions<OllamaOptions> ollamaOptions,
    ILogger<VectorRulebookNarrower> logger) : IRulebookNarrower
{
    private readonly ILlmClient _llmClient = llmClient;
    private readonly QdrantClient _qdrant = qdrant;
    private readonly OllamaOptions _ollamaOptions = ollamaOptions.Value;
    private readonly ILogger<VectorRulebookNarrower> _logger = logger;

    /// <summary>Embeds and indexes every rule of a destination so it can be found by similarity.</summary>
    public async Task EnsureIndexedAsync(DestinationRulebook rulebook, CancellationToken cancellationToken = default)
    {
        var collection = CollectionFor(rulebook.Destination);
        await _qdrant.EnsureCollectionAsync(collection, cancellationToken).ConfigureAwait(false);

        foreach (var rule in rulebook.Rules)
        {
            var vector = await _llmClient
                .EmbedAsync(new LlmRequest(_ollamaOptions.EmbeddingModel, $"{rule.Category}: {rule.Rule}", JsonMode: false), cancellationToken)
                .ConfigureAwait(false);

            await _qdrant.UpsertAsync(collection, rule.RuleId, vector, new Dictionary<string, string>
            {
                ["rule_id"] = rule.RuleId,
                ["destination"] = rulebook.Destination,
                ["rule"] = rule.Rule,
            }, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Indexed {Count} rule(s) for {Destination} into Qdrant", rulebook.Rules.Count, rulebook.Destination);
    }

    public async Task<DestinationRulebook> NarrowAsync(
        DestinationRulebook rulebook,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (rulebook.Rules.Count <= 2)
        {
            return rulebook;
        }

        try
        {
            var vector = await _llmClient
                .EmbedAsync(new LlmRequest(_ollamaOptions.EmbeddingModel, query, JsonMode: false), cancellationToken)
                .ConfigureAwait(false);

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
}
