using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Fuse.Shared.Authorization;

namespace Fuse.UI.Auth;

/// <summary>
/// Page-level permission gate (prerendering and WebAssembly alike). Only decides what the UI
/// shows; every API call is authorised again on the server.
/// </summary>
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "Permission:";

    public RequirePermissionAttribute(string action, string resource) =>
        Policy = PolicyPrefix + FusePermission.NameFor(action, resource);
}

/// <summary>Turns <c>Permission:Permissions.X.Y</c> policy names into a permission-claim requirement.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            return await base.GetPolicyAsync(policyName);

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(FuseClaims.Permission, policyName[RequirePermissionAttribute.PolicyPrefix.Length..])
            .Build();
    }
}
