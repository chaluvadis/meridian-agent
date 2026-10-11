namespace MeridianAgent.Web.Components.Layout
{
    public partial class MainLayout
    {
        private string _location = string.Empty;
        private bool _showGlance;
        private bool _locating;
        private bool _hasPosition;

        protected override async Task OnInitializedAsync()
        {
            try
            {
                var profile = await ProfileService.GetProfileAsync("default");

                // The bar shows where the traveler is, resolved by the same single rule the chat
                // header and the pipeline use so they cannot drift apart.
                _location = profile?.EffectiveLocation ?? string.Empty;
                _hasPosition = profile?.HasCoordinates ?? false;
            }
            catch (Exception)
            {
                // The chat is the point; an unreadable profile must not blank the whole page.
            }
        }

        private void GoToProfileEdit() => Navigation.NavigateTo("/profile/edit");

        private void OpenGlance() => _showGlance = true;

        /// <summary>
        /// Finds out where the traveler is and remembers it. Stays on the prompt for a refusal or a
        /// timeout rather than hiding it, so a traveler who dismissed the browser box by accident can
        /// try again without a reload.
        /// </summary>
        private async Task LocateAsync()
        {
            if (_locating)
            {
                return;
            }

            _locating = true;

            try
            {
                var position = await Geolocation.GetPositionAsync();
                var profile = await ProfileService.GetProfileAsync(IUserProfileService.DefaultProfileId);

                if (position is null || profile is null)
                {
                    return;
                }

                var located = profile with
                {
                    Latitude = position.Latitude,
                    Longitude = position.Longitude,
                };

                await ProfileService.SaveProfileAsync(located);

                _hasPosition = located.HasCoordinates;
            }
            catch (Exception)
            {
                // A refusal or a failed save leaves the app exactly as it was: still the city they typed.
            }
            finally
            {
                _locating = false;
            }
        }
    }
}