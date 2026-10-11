namespace MeridianAgent.Web.Components;

using Microsoft.JSInterop;

/// <summary>Sets the browser tab title from a component that has just rendered.</summary>
public static class BrowserTitle
{
    /// <summary>
    /// Sets the document title the first time a component renders. Guarded because a component can
    /// render during prerendering, before a circuit exists, and again after a reconnect: the title
    /// is decoration, not a dependency, so losing it must never take the page down.
    /// </summary>
    public static async Task SetAsync(IJSRuntime runtime, bool firstRender, string title)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            await runtime.InvokeVoidAsync("setDocumentTitle", title);
        }
        catch (JSException)
        {
            // Prerendering, a dropped circuit, or a missing script: leave the default title alone.
        }
    }
}
