using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Fuse.Domain.Auditing;
using Fuse.Domain.Identity;

namespace Fuse.Infrastructure.Persistence.Configuration;

// MultiTenantIdentityDbContext marks every Identity type multi-tenant; these only fix table names.

internal sealed class ApplicationUserConfig : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("Users", SchemaNames.Identity);
        builder.Property(u => u.ObjectId).HasMaxLength(256);
    }
}

internal sealed class ApplicationRoleConfig : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> builder) => builder.ToTable("Roles", SchemaNames.Identity);
}

internal sealed class ApplicationRoleClaimConfig : IEntityTypeConfiguration<ApplicationRoleClaim>
{
    public void Configure(EntityTypeBuilder<ApplicationRoleClaim> builder) => builder.ToTable("RoleClaims", SchemaNames.Identity);
}

internal sealed class IdentityUserRoleConfig : IEntityTypeConfiguration<IdentityUserRole<string>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<string>> builder) => builder.ToTable("UserRoles", SchemaNames.Identity);
}

internal sealed class IdentityUserClaimConfig : IEntityTypeConfiguration<IdentityUserClaim<string>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<string>> builder) => builder.ToTable("UserClaims", SchemaNames.Identity);
}

internal sealed class IdentityUserLoginConfig : IEntityTypeConfiguration<IdentityUserLogin<string>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<string>> builder) => builder.ToTable("UserLogins", SchemaNames.Identity);
}

internal sealed class IdentityUserTokenConfig : IEntityTypeConfiguration<IdentityUserToken<string>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<string>> builder) => builder.ToTable("UserTokens", SchemaNames.Identity);
}

internal sealed class AuditTrailConfig : IEntityTypeConfiguration<Trail>
{
    public void Configure(EntityTypeBuilder<Trail> builder) => builder.ToTable("AuditTrails", SchemaNames.Auditing).IsMultiTenant();
}
