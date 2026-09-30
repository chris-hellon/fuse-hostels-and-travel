using Microsoft.Data.SqlClient;

namespace Fuse.Infrastructure.Persistence;

/// <summary>
/// While the rebuild is in progress it must only ever touch a local development database.
/// Startup (and the EF tooling) refuse any other server unless <c>Database:AllowNonLocalServer</c>
/// is explicitly set to true.
/// </summary>
public static class DatabaseSafety
{
    private static readonly string[] LocalHosts = ["localhost", "127.0.0.1", "::1", ".", "(local)", "(localdb)"];

    public static void EnsureLocal(string connectionString, bool allowNonLocal)
    {
        if (allowNonLocal || IsLocal(connectionString))
            return;
        throw new InvalidOperationException(
            $"Refusing to connect to database server '{new SqlConnectionStringBuilder(connectionString).DataSource}': only a local development database is allowed. " +
            "Set Database:AllowNonLocalServer=true to override (deliberately).");
    }

    /// <summary>Whether the connection is to a database on this machine (not a shared or live one).</summary>
    public static bool IsLocal(string connectionString)
    {
        var dataSource = new SqlConnectionStringBuilder(connectionString).DataSource;
        var host = dataSource.Replace("tcp:", "", StringComparison.OrdinalIgnoreCase).Split(',', '\\')[0].Trim();
        return LocalHosts.Any(h => host.StartsWith(h, StringComparison.OrdinalIgnoreCase));
    }
}
