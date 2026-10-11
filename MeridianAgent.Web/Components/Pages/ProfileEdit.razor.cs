namespace MeridianAgent.Web.Components.Pages
{
    public partial class ProfileEdit
    {
        private void GoBack() => Navigation.NavigateTo("/");

        protected override Task OnAfterRenderAsync(bool firstRender) =>
            BrowserTitle.SetAsync(JSRuntime, firstRender, "Edit profile — The Meridian Agent");
    }
}