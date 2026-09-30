namespace Fuse.Application.Common.Behaviours;

/// <summary>
/// Applies the contract's access rule to every dispatch, including in-process ones (prerendering,
/// jobs) that never pass through an HTTP endpoint.
/// </summary>
public sealed class AuthorizationBehaviour<TMessage, TResponse>(IAccessEvaluator access) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        switch (await access.EvaluateAsync(typeof(TMessage), cancellationToken))
        {
            case AccessResult.Unauthenticated:
                throw new UnauthorizedException();
            case AccessResult.Forbidden:
                throw new ForbiddenException();
        }

        return await next(message, cancellationToken);
    }
}
