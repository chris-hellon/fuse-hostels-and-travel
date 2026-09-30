namespace Fuse.Contracts.Api;

/// <summary>
/// The only way UI code talks to the application. On the server (prerendering, jobs) it
/// dispatches in-process; in the browser it calls the contract's HTTP endpoint. Both paths
/// enforce the contract's access attribute and raise the same exception types.
/// </summary>
public interface IApi
{
    ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);

    ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);

    ValueTask Send(ICommand command, CancellationToken cancellationToken = default);
}
