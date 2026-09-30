using Microsoft.Extensions.Caching.Memory;

namespace Fuse.Application.Common.Security;

public interface IPermissionService
{
    /// <summary>Permission claim values (<c>Permissions.{Resource}.{Action}</c>) granted through the user's roles.</summary>
    ValueTask<IReadOnlySet<string>> GetPermissionsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Call after changing a user's roles.</summary>
    void Invalidate(string userId);

    /// <summary>Call after changing a role's permissions (affects every member).</summary>
    Task InvalidateRoleAsync(string roleId, CancellationToken cancellationToken = default);
}

internal sealed class PermissionService(IAppDbContext db, IMemoryCache cache) : IPermissionService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public async ValueTask<IReadOnlySet<string>> GetPermissionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(Key(userId), out IReadOnlySet<string>? cached) && cached is not null)
            return cached;

        var permissions = await (
                from userRole in db.UserRoles
                join claim in db.RoleClaims on userRole.RoleId equals claim.RoleId
                where userRole.UserId == userId && claim.ClaimType == FuseClaims.Permission && claim.ClaimValue != null
                select claim.ClaimValue!)
            .Distinct()
            .ToListAsync(cancellationToken);

        IReadOnlySet<string> result = permissions.ToHashSet(StringComparer.Ordinal);
        cache.Set(Key(userId), result, Lifetime);
        return result;
    }

    public void Invalidate(string userId) => cache.Remove(Key(userId));

    public async Task InvalidateRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var members = await db.UserRoles.Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId).ToListAsync(cancellationToken);
        foreach (var userId in members)
            Invalidate(userId);
    }

    private static string Key(string userId) => $"permissions:{userId}";
}
