namespace MeridianAgent.Infrastructure.Services;

using MeridianAgent.Application.Abstractions;

/// <summary>
/// Profile lifecycle for the onboarding wizard: loads the stored profile, detects first-time runs
/// (no completed onboarding), saves completed profiles, and clears the one-time welcome flag.
/// Thin on purpose — all persistence stays in <see cref="IUserProfileStore" />.
/// </summary>
public sealed class UserProfileService : IUserProfileService
{
    private readonly IUserProfileStore _store;
    private readonly ILogger<UserProfileService> _logger;

    public UserProfileService(IUserProfileStore store, ILogger<UserProfileService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public Task<UserProfile?> GetProfileAsync(string profileId = IUserProfileService.DefaultProfileId, CancellationToken cancellationToken = default) =>
        _store.GetAsync(profileId, cancellationToken);

    public async Task<UserProfile> GetOrCreateAsync(string profileId = IUserProfileService.DefaultProfileId, CancellationToken cancellationToken = default)
    {
        var profile = await _store.GetAsync(profileId, cancellationToken).ConfigureAwait(false);
        return profile ?? UserProfile.Default with { Id = profileId };
    }

    public async Task<bool> HasCompletedProfileAsync(string profileId = IUserProfileService.DefaultProfileId, CancellationToken cancellationToken = default)
    {
        var profile = await _store.GetAsync(profileId, cancellationToken).ConfigureAwait(false);
        var completed = profile?.OnboardingCompleted == true;

        if (!completed)
        {
            _logger.LogInformation("No completed profile for {ProfileId}; first-time onboarding applies", profileId);
        }

        return completed;
    }

    public async Task SaveProfileAsync(UserProfile profile, CancellationToken cancellationToken = default)
    {
        await _store.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Profile {ProfileId} saved via onboarding (onboarding completed: {Completed})",
            profile.Id,
            profile.OnboardingCompleted);
    }

    public async Task MarkWelcomeSeenAsync(string profileId = IUserProfileService.DefaultProfileId, CancellationToken cancellationToken = default)
    {
        var profile = await _store.GetAsync(profileId, cancellationToken).ConfigureAwait(false);

        if (profile is null || !profile.PendingWelcome)
        {
            return;
        }

        await _store.SaveAsync(profile with { PendingWelcome = false }, cancellationToken).ConfigureAwait(false);
    }
}
