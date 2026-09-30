using Finbuckle.MultiTenant.EntityFrameworkCore.Stores;
using Fuse.Infrastructure.Persistence.Configuration;

namespace Fuse.Infrastructure.Multitenancy;

public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options)
    : EFCoreStoreDbContext<FuseTenantInfo>(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<FuseTenantInfo>(tenant =>
        {
            tenant.ToTable("Tenants", SchemaNames.MultiTenancy);
            tenant.Property(t => t.Id).HasMaxLength(64);
        });
    }
}
