namespace MeridianAgent.Web.Components.Pages
{
    public partial class Error
    {
        [CascadingParameter]
        private HttpContext? HttpContext { get; set; }

        [PersistentState]
        public string? RequestId { get; set; }

        private bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        protected override void OnInitialized()
        {
            RequestId ??= Activity.Current?.Id ?? HttpContext?.TraceIdentifier;
        }
    }
}