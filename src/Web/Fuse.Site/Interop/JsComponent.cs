using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Fuse.Site.Interop;

/// <summary>A component that uses the site's JS module (<see cref="Js"/>, imported on first use and released with it).</summary>
public abstract class JsComponent : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    private FuseJs? _js;

    protected FuseJs Js => _js ??= new FuseJs(JSRuntime);

    /// <summary>
    /// Clean-up that needs the module (e.g. unhooking listeners). Runs only if the module was used, so
    /// never while prerendering; the page may already be gone.
    /// </summary>
    protected virtual Task OnDisposeAsync() => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (_js is null)
            return;
        try
        {
            await OnDisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The page is going away.
        }
        await _js.DisposeAsync();
    }
}
