namespace MeridianAgent.Web.Components.Onboarding;

/// <summary>
/// Onboarding captures a brand-new traveler; Edit re-opens the same wizard over an existing
/// profile inside the main layout. The button copy and the profile flags differ, the steps do not.
/// </summary>
public enum WizardMode
{
    Onboarding,
    Edit,
}
