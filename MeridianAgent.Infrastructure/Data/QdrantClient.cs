namespace MeridianAgent.Infrastructure.Data;

/// <summary>A point returned by a Qdrant similarity search.</summary>
public sealed record QdrantHit(string Id, double Score, IReadOnlyDictionary<string, string> Payload);

/// <summary>
/// Minimal Qdrant REST client (v1 API): collection bootstrap, point upsert, and vector search.
/// Deliberately dependency-free so the solution restores on the .NET 11 RC feed without extra packages.
/// </summary>
public sealed class QdrantClient
{
    private readonly HttpClient _httpClient;
    private readonly QdrantOptions _options;
    private readonly ILogger<QdrantClient> _logger;
    private readonly HashSet<string> _knownCollections = new(StringComparer.OrdinalIgnoreCase);

    public QdrantClient(HttpClient httpClient, IOptions<QdrantOptions> options, ILogger<QdrantClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_options.Endpoint.TrimEnd('/'));

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("api-key", _options.ApiKey);
        }
    }

    public async Task EnsureCollectionAsync(string collection, CancellationToken cancellationToken = default)
    {
        if (_knownCollections.Contains(collection))
        {
            return;
        }

        if (await ExistsWithDifferentSizeAsync(collection, cancellationToken).ConfigureAwait(false))
        {
            // A collection from a previous embedding model: its vectors no longer match. Rebuilt
            // rather than reused, so switching models heals itself instead of failing every upsert
            // with an error the rulebook search would swallow into a full-rulebook fallback.
            _logger.LogWarning(
                "Qdrant collection {Collection} holds vectors of a different size; rebuilding it for the current embedding model",
                collection);

            using var delete = await _httpClient
                .DeleteAsync($"collections/{Uri.EscapeDataString(collection)}", cancellationToken)
                .ConfigureAwait(false);

            if (!delete.IsSuccessStatusCode)
            {
                var deleteError = await delete.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"Qdrant could not drop the mismatched collection '{collection}': {(int)delete.StatusCode} {deleteError}");
            }
        }

        var body = new
        {
            vectors = new Dictionary<string, object>
            {
                ["size"] = _options.VectorSize,
                ["distance"] = _options.Distance,
            },
        };

        using var response = await _httpClient
            .PutAsJsonAsync($"collections/{Uri.EscapeDataString(collection)}", body, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Qdrant could not create collection '{collection}': {(int)response.StatusCode} {error}");
        }

        _knownCollections.Add(collection);
        _logger.LogInformation("Qdrant collection {Collection} is ready", collection);
    }

    /// <summary>
    /// Whether the collection already exists holding vectors of a different length than the
    /// configured embedding model produces. A missing collection is not a mismatch: it is about to
    /// be created at the right size.
    /// </summary>
    private async Task<bool> ExistsWithDifferentSizeAsync(string collection, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync($"collections/{Uri.EscapeDataString(collection)}", cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        try
        {
            var document = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
            var configSize = document
                .GetProperty("result")
                .GetProperty("config")
                .GetProperty("params")
                .GetProperty("vectors")
                .GetProperty("size")
                .GetInt32();

            return configSize != _options.VectorSize;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A response shape we do not understand is treated as "no mismatch": creating the
            // collection again is idempotent, and refusing to index would be worse.
            _logger.LogDebug(ex, "Could not read the vector size of Qdrant collection {Collection}", collection);
            return false;
        }
    }

    public async Task UpsertAsync(
        string collection,
        string id,
        float[] vector,
        IReadOnlyDictionary<string, string>? payload = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(collection, cancellationToken).ConfigureAwait(false);

        var body = new
        {
            points = new[]
            {
                new
                {
                    id,
                    vector,
                    payload = payload ?? new Dictionary<string, string>(),
                },
            },
        };

        using var response = await _httpClient
            .PutAsync($"collections/{Uri.EscapeDataString(collection)}/points?wait=true", JsonContent.Create(body), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Qdrant upsert failed: {(int)response.StatusCode} {error}");
        }
    }

    public async Task<IReadOnlyList<QdrantHit>> SearchAsync(
        string collection,
        float[] vector,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            vector,
            limit,
            with_payload = true,
        };

        using var response = await _httpClient
            .PostAsJsonAsync($"collections/{Uri.EscapeDataString(collection)}/points/search", body, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("Qdrant search in {Collection} failed: {Status} {Error}", collection, (int)response.StatusCode, error);
            return [];
        }

        var result = await response.Content
            .ReadFromJsonAsync<QdrantSearchResponse>(cancellationToken)
            .ConfigureAwait(false);

        var hits = new List<QdrantHit>();
        foreach (var point in result?.Result ?? [])
        {
            var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (point.Payload is not null)
            {
                foreach (var (key, value) in point.Payload)
                {
                    payload[key] = value.ValueKind == JsonValueKind.String
                        ? value.GetString() ?? string.Empty
                        : value.ToString();
                }
            }

            hits.Add(new QdrantHit(point.Id, point.Score, payload));
        }

        _logger.LogInformation("Qdrant search in {Collection} returned {Count} hit(s)", collection, hits.Count);
        return hits;
    }

    private sealed record QdrantScoredPoint
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("score")]
        public double Score { get; init; }

        [JsonPropertyName("payload")]
        public Dictionary<string, JsonElement>? Payload { get; init; }
    }

    private sealed record QdrantSearchResponse
    {
        [JsonPropertyName("result")]
        public QdrantScoredPoint[]? Result { get; init; }
    }
}
