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
            return Array.Empty<QdrantHit>();
        }

        var result = await response.Content
            .ReadFromJsonAsync<QdrantSearchResponse>(cancellationToken)
            .ConfigureAwait(false);

        var hits = new List<QdrantHit>();
        foreach (var point in result?.Result ?? Array.Empty<QdrantScoredPoint>())
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
