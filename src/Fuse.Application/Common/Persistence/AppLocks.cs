using Dapper;

namespace Fuse.Application.Common.Persistence;

/// <summary>
/// SQL Server application locks (sp_getapplock): one piece of work at a time across every request and server, with no
/// table or schema of its own. The lock belongs to the unit of work's connection, which stays open until it's released.
/// </summary>
public static class AppLocks
{
    /// <summary>Waits (up to <paramref name="timeout"/>) for the exclusive lock on <paramref name="resource"/>; dispose to release it.</summary>
    public static async Task<IAsyncDisposable> LockAsync(this IAppDbContext db, string resource, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var result = await db.Database.GetDbConnection().ExecuteScalarAsync<int>(new CommandDefinition(
                """
                DECLARE @result int;
                EXEC @result = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @Timeout;
                SELECT @result;
                """,
                new { Resource = resource, Timeout = (int)timeout.TotalMilliseconds }, cancellationToken: cancellationToken));
            // 0 = granted, 1 = granted after waiting; negative = timed out, cancelled or deadlocked.
            if (result < 0)
                throw new BusinessRuleException("This is being processed already. Please try again in a moment.");
            return new Lock(db, resource);
        }
        catch
        {
            await db.Database.CloseConnectionAsync();
            throw;
        }
    }

    private sealed class Lock(IAppDbContext db, string resource) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await db.Database.GetDbConnection().ExecuteAsync(new CommandDefinition(
                    "EXEC sp_releaseapplock @Resource = @Resource, @LockOwner = 'Session';", new { Resource = resource }));
            }
            finally
            {
                // (A release only fails when the connection broke, which ended the session and its lock with it.)
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}
