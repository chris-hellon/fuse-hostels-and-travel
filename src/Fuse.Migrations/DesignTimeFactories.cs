using System.Security.Claims;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Fuse.Application.Common.Security;
using Fuse.Infrastructure.Multitenancy;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Multitenancy;

namespace Fuse.Migrations;

/// <summary>
/// Lets <c>dotnet ef</c> build the contexts without the web host. Adding and scripting
/// migrations never connects; only <c>database update</c> uses this connection string.
/// </summary>
internal static class DesignTime
{
    public static string ConnectionString
    {
        get
        {
            var connectionString = Environment.GetEnvironmentVariable("TRAVALOUD_DESIGN_CONNECTION")
                ?? "Server=localhost;Database=travaloud;User Id=sa;Password=SuperHard123;TrustServerCertificate=True;Encrypt=False;";
            DatabaseSafety.EnsureLocal(connectionString,
                Environment.GetEnvironmentVariable("TRAVALOUD_ALLOW_NON_LOCAL_DB") == "true");
            return connectionString;
        }
    }
}

internal sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(DesignTime.ConnectionString, sql => sql.MigrationsAssembly(typeof(DesignTime).Assembly.GetName().Name))
            .Options;

        var tenant = new FuseTenantInfo { Id = MultitenancyConstants.Fuse.Id, Identifier = MultitenancyConstants.Fuse.Identifier };
        return new AppDbContext(new FixedTenantContextAccessor(tenant), options, new DesignTimeUser(), new NoPublisher());
    }

    private sealed class DesignTimeUser : ICurrentUser
    {
        public ClaimsPrincipal Principal { get; } = new();
        public bool IsAuthenticated => false;
        public Guid? UserId => null;
        public string? Email => null;
        public bool IsInRole(string role) => false;
    }

    private sealed class NoPublisher : IPublisher
    {
        public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => ValueTask.CompletedTask;

        public ValueTask Publish(object notification, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}

internal sealed class TenantDbContextDesignTimeFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlServer(DesignTime.ConnectionString, sql => sql.MigrationsAssembly(typeof(DesignTime).Assembly.GetName().Name))
            .Options);
}
