namespace MeridianAgent.Web.Components.Pages
{
    public partial class Onboarding
    {
        protected override Task OnAfterRenderAsync(bool firstRender) =>
            BrowserTitle.SetAsync(JSRuntime, firstRender, "Welcome — The Meridian Agent");
    }
}