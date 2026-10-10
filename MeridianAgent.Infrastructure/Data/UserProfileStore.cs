using System.Text.Json;
using System.Text.Json.Serialization;
using MeridianAgent.Application.Abstractions;
using MeridianAgent.Domain.Entities;
using MeridianAgent.Infrastructure.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MeridianAgent.Infrastructure.Data;

/// <summary>JSON file-backed user profile storage under {DataDirectory}/profiles/{id}.json.</summary>
public sealed class JsonFileUserProfileStore : IUserProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly StorageOptions _options;
    private readonly string _dataDirectory;
    private readonly ILogger<JsonFileUserProfileStore> _logger;

    public JsonFileUserProfileStore(IOptions<StorageOptions> options, ILogger<JsonFileUserProfileStore> logger)
    {
        _options = options.Value;
        _dataDirectory = DataPaths.Resolve(_options.DataDirectory);
        _logger = logger;
    }

    public async Task<UserProfile?> GetAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(profileId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var profile = await JsonSerializer
            .DeserializeAsync<UserProfile>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        if (profile is not null)
        {
            _logger.LogInformation("Loaded profile {ProfileId} from {Path}", profileId, path);
        }

        return profile;
    }

    public async Task SaveAsync(UserProfile profile, CancellationToken cancellationToken = default)
    {
        var path = PathFor(profile.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, profile, SerializerOptions, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Saved profile {ProfileId} to {Path}", profile.Id, path);
    }

    public async Task<IReadOnlyList<UserProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        var directory = Directory.CreateDirectory(ProfilesDirectory);
        var profiles = new List<UserProfile>();

        foreach (var file in directory.GetFiles("*.json"))
        {
            await using var stream = file.OpenRead();
            var profile = await JsonSerializer.DeserializeAsync<UserProfile>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            if (profile is not null)
            {
                profiles.Add(profile);
            }
        }

        return profiles;
    }

    private string ProfilesDirectory => Path.Combine(_dataDirectory, _options.ProfilesSubdirectory);

    private string PathFor(string profileId)
    {
        var safe = string.Join('_', (profileId ?? "default").Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(ProfilesDirectory, $"{safe}.json");
    }
}
