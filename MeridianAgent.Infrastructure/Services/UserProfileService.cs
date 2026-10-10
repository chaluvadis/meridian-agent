namespace MeridianAgent.Infrastructure.Services;

using MeridianAgent.Application.Abstractions;
using MeridianAgent.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Profile lifecycle backed by SQLite through EF Core. The service is a singleton (route guards and
/// the proactive scheduler share it), so it takes an <see cref="IDbContextFactory{AppDbContext}" />:
/// contexts are short-lived and created per operation, which keeps the singleton safe.
/// </summary>
public sealed class UserProfileService : IUserProfileService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<UserProfileService> _logger;

    public UserProfileService(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<UserProfileService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<UserProfile?> GetProfileAsync(
        string profileId = IUserProfileService.DefaultProfileId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await context.Profiles
            .AsNoTracking()
            .FirstOrDefaultAsync(profile => profile.Id == profileId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<UserProfile> GetOrCreateAsync(
        string profileId = IUserProfileService.DefaultProfileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        return profile ?? UserProfile.Default with { Id = profileId };
    }

    public async Task<bool> HasCompletedProfileAsync(
        string profileId = IUserProfileService.DefaultProfileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        var completed = profile?.OnboardingCompleted == true;

        if (!completed)
        {
            _logger.LogInformation("No stored profile for {ProfileId}; first-time onboarding applies", profileId);
        }

        return completed;
    }

    public async Task SaveProfileAsync(UserProfile profile, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var existing = await context.Profiles
            .FirstOrDefaultAsync(stored => stored.Id == profile.Id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            context.Profiles.Add(profile);
        }
        else
        {
            // The profile is replaced wholesale, so every column is refreshed on save.
            context.Entry(existing).CurrentValues.SetValues(profile);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Profile {ProfileId} stored in SQLite (onboarding completed: {Completed})",
            profile.Id,
            profile.OnboardingCompleted);
    }

    public async Task MarkWelcomeSeenAsync(
        string profileId = IUserProfileService.DefaultProfileId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var profile = await context.Profiles
            .FirstOrDefaultAsync(stored => stored.Id == profileId, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null || !profile.PendingWelcome)
        {
            return;
        }

        context.Entry(profile).CurrentValues.SetValues(profile with { PendingWelcome = false });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
