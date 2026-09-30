using System.Diagnostics;

namespace Fuse.Application.Common.Behaviours;

public sealed class LoggingBehaviour<TMessage, TResponse>(ILogger<LoggingBehaviour<TMessage, TResponse>> logger)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    private static readonly TimeSpan SlowThreshold = TimeSpan.FromMilliseconds(750);

    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            return await next(message, cancellationToken);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            if (elapsed > SlowThreshold)
                logger.LogWarning("Slow request {Request} took {ElapsedMs:N0} ms", typeof(TMessage).Name, elapsed.TotalMilliseconds);
            else
                logger.LogDebug("{Request} took {ElapsedMs:N0} ms", typeof(TMessage).Name, elapsed.TotalMilliseconds);
        }
    }
}
