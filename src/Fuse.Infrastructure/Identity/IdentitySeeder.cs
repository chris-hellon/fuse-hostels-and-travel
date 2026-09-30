using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Fuse.Domain.Identity;
using Fuse.Infrastructure.Multitenancy;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Authorization;
using Fuse.Shared.Multitenancy;

namespace Fuse.Infrastructure.Identity;

/// <summary>
/// Runs at every startup, additively (as the legacy seeder did): creates missing built-in roles,
/// grants each built-in role any default permission it lacks, and creates the tenant admin if
/// there isn't one (only when <c>Seed:AdminPassword</c> is configured: there is no built-in default
/// password). It never removes anything.
/// </summary>
internal sealed class IdentitySeeder(
    AppDbContext db,
    RoleManager<ApplicationRole> roles,
    UserManager<ApplicationUser> users,
    IConfiguration configuration,
    ILogger<IdentitySeeder> logger)
{
    public async Task SeedAsync(FuseTenantInfo tenant, CancellationToken cancellationToken)
    {
        foreach (var roleName in FuseRoles.DefaultRoles)
        {
            var role = await roles.FindByNameAsync(roleName);
            if (role is null)
            {
                logger.LogInformation("Seeding {Role} role for tenant {Tenant}", roleName, tenant.Id);
                role = new ApplicationRole(roleName, $"{roleName} Role for {tenant.Id} Tenant");
                await roles.CreateAsync(role);
            }

            await GrantMissingAsync(role, DefaultPermissionsFor(roleName, tenant), cancellationToken);
        }

        await EnsureAdminAsync(tenant);
    }

    private static IEnumerable<FusePermission> DefaultPermissionsFor(string roleName, FuseTenantInfo tenant) => roleName switch
    {
        FuseRoles.Admin when tenant.Id == MultitenancyConstants.Root.Id => [.. FusePermissions.Admin, .. FusePermissions.Root],
        FuseRoles.Admin => FusePermissions.Admin,
        FuseRoles.Basic => FusePermissions.Basic,
        FuseRoles.TourManager => FusePermissions.TourManager,
        FuseRoles.PropertyManager => FusePermissions.PropertyManager,
        FuseRoles.Supplier => FusePermissions.Supplier,
        _ => [],
    };

    private async Task GrantMissingAsync(ApplicationRole role, IEnumerable<FusePermission> permissions, CancellationToken cancellationToken)
    {
        var existing = (await db.RoleClaims
                .Where(c => c.RoleId == role.Id && c.ClaimType == FuseClaims.Permission)
                .Select(c => c.ClaimValue)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var missing = permissions.Where(p => !existing.Contains(p.Name)).ToList();
        if (missing.Count == 0)
            return;

        logger.LogInformation("Granting {Count} default permission(s) to {Role}", missing.Count, role.Name);
        db.RoleClaims.AddRange(missing.Select(p => new ApplicationRoleClaim
        {
            RoleId = role.Id,
            ClaimType = FuseClaims.Permission,
            ClaimValue = p.Name,
            CreatedBy = nameof(IdentitySeeder),
            CreatedOn = DateTime.UtcNow,
        }));
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureAdminAsync(FuseTenantInfo tenant)
    {
        if (string.IsNullOrWhiteSpace(tenant.AdminEmail))
            return;

        var admin = await users.FindByEmailAsync(tenant.AdminEmail);
        if (admin is null)
        {
            var password = configuration["Seed:AdminPassword"];
            if (string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning("Tenant {Tenant} has no admin user and Seed:AdminPassword is not set; skipping", tenant.Id);
                return;
            }

            var userName = $"{tenant.Id.Trim()}.{FuseRoles.Admin}".ToLowerInvariant();
            admin = new ApplicationUser
            {
                FirstName = tenant.Id.Trim().ToLowerInvariant(),
                LastName = FuseRoles.Admin,
                FullName = $"{tenant.Id.Trim().ToLowerInvariant()} {FuseRoles.Admin}",
                Email = tenant.AdminEmail,
                UserName = userName,
                EmailConfirmed = true,
                PhoneNumberConfirmed = true,
                IsActive = true,
            };
            logger.LogWarning("Creating the default admin user for tenant {Tenant}; change its password", tenant.Id);
            await users.CreateAsync(admin, password);
        }

        if (!await users.IsInRoleAsync(admin, FuseRoles.Admin))
            await users.AddToRoleAsync(admin, FuseRoles.Admin);
    }
}
