using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace Fuse.Infrastructure.Mail;

/// <summary>Renders an email component to HTML (Blazor's HtmlRenderer, no browser needed).</summary>
internal static class EmailRenderer
{
    public static async Task<string> RenderAsync<TComponent>(IServiceProvider services, ILoggerFactory logs, Dictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(services, logs);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters))).ToHtmlString());
    }
}
