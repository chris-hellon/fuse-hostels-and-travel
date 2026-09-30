using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Fuse.Infrastructure.Identity;
using Fuse.Infrastructure.Multitenancy;

namespace Fuse.Infrastructure.Persistence;

/// <summary>
/// Startup: applies pending migrations, then seeds roles, permissions and the tenant admin,
/// as the legacy app did. Stored procedures and other hand-made objects live in the database itself.
/// </summary>
public static class DatabaseMigrator
{
    public static async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Database.MigrateAsync(cancellationToken);
        }

        await ForEachTenantAsync(services, migrate: true, cancellationToken);
    }

    /// <summary>Seeding only, for a database whose schema was created some other way (integration tests).</summary>
    public static Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default) =>
        ForEachTenantAsync(services, migrate: false, cancellationToken);

    private static async Task ForEachTenantAsync(IServiceProvider services, bool migrate, CancellationToken cancellationToken)
    {
        IEnumerable<FuseTenantInfo> tenants;
        await using (var scope = services.CreateAsyncScope())
        {
            // Only the deployed tenant. The table may hold others (root/test from the platform days);
            // they share this database, so they're migrated with it, but never seeded.
            var fuse = await scope.ServiceProvider.GetRequiredService<IMultiTenantStore<FuseTenantInfo>>()
                .GetAsync(Shared.Multitenancy.MultitenancyConstants.Fuse.Id);
            tenants = fuse is null ? [] : [fuse];
        }

        foreach (var tenant in tenants)
        {
            // Everything resolved in this async flow (DbContext, UserManager, ...) binds to this tenant.
            services.GetRequiredService<IMultiTenantContextSetter>().MultiTenantContext =
                new MultiTenantContext<FuseTenantInfo>(tenant);

            await using var scope = services.CreateAsyncScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseMigrator));
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var pending = migrate ? (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList() : [];
            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} migration(s) for tenant {Tenant}: {Migrations}", pending.Count, tenant.Id, pending);
                await db.Database.MigrateAsync(cancellationToken);
            }

            await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(tenant, cancellationToken);
        }

        services.GetRequiredService<IMultiTenantContextSetter>().MultiTenantContext = new MultiTenantContext<FuseTenantInfo>(null);
    }
}
