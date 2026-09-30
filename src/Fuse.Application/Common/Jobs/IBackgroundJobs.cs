using System.Linq.Expressions;

namespace Fuse.Application.Common.Jobs;

/// <summary>
/// Work that shouldn't hold up (or fail) the request: emails, outgoing webhooks. Queued as a call on a service
/// (Hangfire, in its own schema, so it survives restarts and is retried); the call's arguments are stored, so pass
/// plain data. Jobs get no tenant or user context: load or render what they need before enqueueing.
/// </summary>
public interface IBackgroundJobs
{
    /// <summary>Queues <paramref name="job"/>; a <see cref="CancellationToken"/> argument is replaced by the job's own.</summary>
    void Enqueue<TService>(Expression<Func<TService, Task>> job) where TService : notnull;

    /// <summary>As <see cref="Enqueue{TService}"/>, run after <paramref name="delay"/>.</summary>
    void Schedule<TService>(Expression<Func<TService, Task>> job, TimeSpan delay) where TService : notnull;
}
