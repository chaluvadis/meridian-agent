namespace MeridianAgent.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the SQLite-backed profile store for the web project without leaking Entity Framework
/// providers or namespaces into the presentation layer.
/// </summary>
public static class MeridianDatabase
{
    /// <summary>The connection string used when configuration does not provide one.</summary>
    public const string DefaultConnectionString = "Data Source=meridian.db";

    public static IServiceCollection AddMeridianDatabase(
        this IServiceCollection services,
        string? connectionString = null)
    {
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite(connectionString ?? DefaultConnectionString));

        services.AddSingleton<ProfileDatabaseInitializer>();

        return services;
    }
}
