namespace MeridianAgent.Infrastructure.Data;

public sealed class QdrantOptions
{
    public const string SectionName = "Qdrant";

    /// <summary>Qdrant base URL (env: Qdrant__Endpoint).</summary>
    public string Endpoint { get; set; } = "http://localhost:6333";

    /// <summary>Optional API key (env: Qdrant__ApiKey).</summary>
    public string? ApiKey { get; set; }

    public string CollectionPrefix { get; set; } = "meridian";

    public int VectorSize { get; set; } = 768;

    public string Distance { get; set; } = "Cosine";
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Directory holding the JSON profiles, rulebooks, and sample data (env: Storage__DataDirectory).</summary>
    public string DataDirectory { get; set; } = "samples";

    public string ProfilesSubdirectory { get; set; } = "profiles";

    public string RulebooksSubdirectory { get; set; } = "rulebooks";
}
