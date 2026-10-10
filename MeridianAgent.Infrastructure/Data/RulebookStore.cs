using System.Text.Json;
using System.Text.Json.Serialization;
using MeridianAgent.Application.Abstractions;
using MeridianAgent.Domain.Entities;
using MeridianAgent.Infrastructure.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MeridianAgent.Infrastructure.Data;

/// <summary>JSON file-backed destination rulebook storage under {DataDirectory}/rulebooks/{destination}.json.</summary>
public sealed class JsonFileRulebookStore : IRulebookStore
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
    private readonly ILogger<JsonFileRulebookStore> _logger;

    public JsonFileRulebookStore(IOptions<StorageOptions> options, ILogger<JsonFileRulebookStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        _dataDirectory = DataPaths.Resolve(_options.DataDirectory);
    }

    public async Task<DestinationRulebook?> GetAsync(string destination, CancellationToken cancellationToken = default)
    {
        var path = PathFor(destination);
        if (!File.Exists(path))
        {
            _logger.LogInformation("No rulebook file for {Destination} at {Path}", destination, path);
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer
            .DeserializeAsync<DestinationRulebook>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveAsync(DestinationRulebook rulebook, CancellationToken cancellationToken = default)
    {
        var path = PathFor(rulebook.Destination);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, rulebook, SerializerOptions, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Saved rulebook for {Destination} to {Path}", rulebook.Destination, path);
    }

    public async Task<IReadOnlyList<DestinationRulebook>> ListAsync(CancellationToken cancellationToken = default)
    {
        var directory = Directory.CreateDirectory(RulebooksDirectory);
        _logger.LogInformation("Loading destination rulebooks from {Directory}", directory.FullName);
        var rulebooks = new List<DestinationRulebook>();

        foreach (var file in directory.GetFiles("*.json"))
        {
            await using var stream = file.OpenRead();
            var rulebook = await JsonSerializer
                .DeserializeAsync<DestinationRulebook>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            if (rulebook is not null)
            {
                rulebooks.Add(rulebook);
            }
        }

        return rulebooks;
    }

    private string RulebooksDirectory => Path.Combine(_dataDirectory, _options.RulebooksSubdirectory);

    private string PathFor(string destination)
    {
        var safe = string.Join('_', (destination ?? "unknown").Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(RulebooksDirectory, $"{safe}.json");
    }
}
