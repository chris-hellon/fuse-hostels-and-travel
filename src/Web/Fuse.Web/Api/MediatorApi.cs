using Mediator;
using Fuse.Contracts.Api;

namespace Fuse.Web.Api;

/// <summary>
/// <see cref="IApi"/> on the server (prerendering, jobs, webhooks). Each call gets its own DI scope,
/// and so its own DbContext, exactly like an HTTP request would. Components load in parallel while
/// prerendering; sharing the request's DbContext would make EF throw on concurrent use. The caller's
/// user (HttpContext) and tenant (async-local) flow into the new scope.
/// </summary>
internal sealed class MediatorApi(IServiceScopeFactory scopes) : IApi
{
    public async ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(query, cancellationToken);
    }

    public async ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, cancellationToken);
    }

    public async ValueTask Send(ICommand command, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, cancellationToken);
    }
}
