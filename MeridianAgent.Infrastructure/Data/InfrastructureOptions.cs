namespace MeridianAgent.Infrastructure.Data;

public sealed class QdrantOptions
{
    public const string SectionName = "Qdrant";

    /// <summary>Qdrant base URL (env: Qdrant__Endpoint).</summary>
    public string Endpoint { get; set; } = "http://localhost:6333";

    /// <summary>Optional API key (env: Qdrant__ApiKey).</summary>
    public string? ApiKey { get; set; }

    public string CollectionPrefix { get; set; } = "meridian";

    /// <summary>
    /// Vector length of the embedding model — 1536 for text-embedding-3-small (see
    /// <see cref="AiOptions.DefaultVectorSize"/>). A collection written by a different-sized model is
    /// rebuilt on the next indexing run, so this only has to match the model that is configured now.
    /// </summary>
    public int VectorSize { get; set; } = AiOptions.DefaultVectorSize;

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
