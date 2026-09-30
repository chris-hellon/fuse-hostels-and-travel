using Microsoft.JSInterop;

namespace Fuse.Site.Interop;

/// <summary>The site's JavaScript module (<c>wwwroot/js/fuse.js</c>), imported on first use.</summary>
public sealed class FuseJs(IJSRuntime js) : IAsyncDisposable
{
    private Task<IJSObjectReference>? _module;

    private Task<IJSObjectReference> Module =>
        _module ??= js.InvokeAsync<IJSObjectReference>("import", "./_content/Fuse.Site/js/fuse.js").AsTask();

    public async ValueTask InvokeVoidAsync(string identifier, params object?[] args) =>
        await (await Module).InvokeVoidAsync(identifier, args);

    public async ValueTask<T> InvokeAsync<T>(string identifier, params object?[] args) =>
        await (await Module).InvokeAsync<T>(identifier, args);

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
            return;
        try
        {
            await (await _module).DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The page is going away.
        }
    }
}
