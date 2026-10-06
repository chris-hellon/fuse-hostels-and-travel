using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Fuse.Application.Common.Security;
using Fuse.Domain.Identity;
using Fuse.Shared.Authorization;

namespace Fuse.Infrastructure.Identity;

/// <summary>
/// Builds the cookie's identity: user id, email, role names and profile claims (full name, avatar).
/// Deliberately NOT the role-aware base factory, which copies every role claim into the cookie:
/// for an Admin that's ~156 permission claims and an ~11 KB cookie split into chunks, which
/// proxies and some clients reject. Permissions are added per request instead (see below).
/// </summary>
internal sealed class AppClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        foreach (var role in await UserManager.GetRolesAsync(user))
            identity.AddClaim(new Claim(Options.ClaimsIdentity.RoleClaimType, role));

        identity.AddClaim(new Claim(FuseClaims.Fullname, user.DisplayName()));
        if (!string.IsNullOrEmpty(user.FirstName))
            identity.AddClaim(new Claim(ClaimTypes.Name, user.FirstName));
        if (!string.IsNullOrEmpty(user.LastName))
            identity.AddClaim(new Claim(ClaimTypes.Surname, user.LastName));
        if (!string.IsNullOrEmpty(user.ImageUrl))
            identity.AddClaim(new Claim(FuseClaims.ImageUrl, user.ImageUrl));

        return identity;
    }
}

/// <summary>
/// Adds the staff user's permissions (from roles, cached) as claims on every request. Permissions
/// are never baked into the cookie, so role changes apply once the cache is invalidated, and the
/// prerendered UI, the WebAssembly UI and the server all see the same set.
/// </summary>
internal sealed class PermissionClaimsTransformation(IPermissionService permissions) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated is not true || !principal.IsStaff() ||
            principal.HasClaim(c => c.Type == FuseClaims.Permission) || principal.GetUserId() is not { } userId)
        {
            return principal;
        }

        var granted = await permissions.GetPermissionsAsync(userId);
        var identity = new ClaimsIdentity(granted.Select(p => new Claim(FuseClaims.Permission, p)));
        var transformed = principal.Clone();
        transformed.AddIdentity(identity);
        return transformed;
    }
}
