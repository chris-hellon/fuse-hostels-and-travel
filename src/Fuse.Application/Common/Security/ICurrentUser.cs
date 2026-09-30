using System.Security.Claims;

namespace Fuse.Application.Common.Security;

/// <summary>The caller of the current request, HTTP or in-process.</summary>
public interface ICurrentUser
{
    ClaimsPrincipal Principal { get; }

    bool IsAuthenticated { get; }

    /// <summary>Identity user id, or null when anonymous (also used as the audit "system" user).</summary>
    DefaultIdType? UserId { get; }

    string? Email { get; }

    bool IsInRole(string role);
}
