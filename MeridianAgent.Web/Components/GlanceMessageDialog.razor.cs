namespace MeridianAgent.Web.Components
{
    public partial class GlanceMessageDialog
    {
        [CascadingParameter]
        private IMudDialogInstance? Instance { get; set; }

        [Parameter]
        public FavoriteRow Favorite { get; set; } = default!;

        private void Close() => Instance?.Close(DialogResult.Ok(true));
    }
}