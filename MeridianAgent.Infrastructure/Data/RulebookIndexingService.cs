namespace MeridianAgent.Infrastructure.Data;

/// <summary>
/// On startup, indexes every rulebook found in the store into Qdrant. Failures are logged and
/// swallowed: the app runs fine on the file-backed rulebooks without the vector index.
/// </summary>
public sealed class RulebookIndexingService(
    IRulebookStore rulebookStore,
    VectorRulebookNarrower narrower,
    ILogger<RulebookIndexingService> logger) : BackgroundService
{
    private readonly IRulebookStore _rulebookStore = rulebookStore;
    private readonly VectorRulebookNarrower _narrower = narrower;
    private readonly ILogger<RulebookIndexingService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var rulebooks = await _rulebookStore.ListAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("Indexing {Count} destination rulebook(s) into Qdrant", rulebooks.Count);

            foreach (var rulebook in rulebooks)
            {
                await _narrower.EnsureIndexedAsync(rulebook, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rulebook indexing into Qdrant failed; continuing with file-backed rulebooks only");
        }
    }
}
