namespace MeridianAgent.Infrastructure.Data;

using MeridianAgent.Application.Abstractions;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Applies the SQLite schema on startup and imports the JSON profiles written by pre-SQLite builds,
/// so the app never runs against a missing database and existing travelers keep their preferences.
/// </summary>
public sealed class ProfileDatabaseInitializer
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IUserProfileStore _legacyStore;
    private readonly ILogger<ProfileDatabaseInitializer> _logger;

    public ProfileDatabaseInitializer(
        IDbContextFactory<AppDbContext> contextFactory,
        IUserProfileStore legacyStore,
        ILogger<ProfileDatabaseInitializer> logger)
    {
        _contextFactory = contextFactory;
        _legacyStore = legacyStore;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("SQLite profile store migrated ({Provider})", context.Database.ProviderName);

        if (await context.Profiles.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var legacy = await _legacyStore.ListAsync(cancellationToken).ConfigureAwait(false);

        if (legacy.Count == 0)
        {
            return;
        }

        context.Profiles.AddRange(legacy);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Imported {Count} legacy JSON profile(s) into SQLite", legacy.Count);
    }
}
