namespace MeridianAgent.Web.Components
{
    public partial class Routes
    {
        private async Task OnNavigateAsync(NavigationContext context)
        {
            var path = context.Path.Trim('/').ToLowerInvariant();
            var onboarded = await ProfileService.HasCompletedProfileAsync();

            if (!onboarded)
            {
                if (path is not ("onboarding" or "error" or "not-found"))
                {
                    Navigation.NavigateTo("/onboarding", replace: true);
                }

                return;
            }

            if (path is "onboarding")
            {
                // Onboarding is first-time only: returning travelers belong in the chat.
                Navigation.NavigateTo("/", replace: true);
            }
        }
    }
}