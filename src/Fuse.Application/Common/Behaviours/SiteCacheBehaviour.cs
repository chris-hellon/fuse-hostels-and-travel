using Fuse.Application.Features.Site;

namespace Fuse.Application.Common.Behaviours;

/// <summary>Any successful command may have changed what the public site shows, so the site's cache is cleared.</summary>
public sealed class SiteCacheBehaviour<TMessage, TResponse>(SiteCache cache) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next(message, cancellationToken);
        if (message is IBaseCommand)
            cache.Clear();
        return response;
    }
}
