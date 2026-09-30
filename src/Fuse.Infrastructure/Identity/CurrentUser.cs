using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Fuse.Application.Common.Security;
using Fuse.Shared.Authorization;

namespace Fuse.Infrastructure.Identity;

/// <summary>
/// The HTTP request's user, unless code without a request (a Hangfire job, a webhook acting for a
/// service account) has called <see cref="RunAs"/>.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    private ClaimsPrincipal? _override;

    public ClaimsPrincipal Principal => _override ?? httpContextAccessor.HttpContext?.User ?? Anonymous;

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated is true;

    public Guid? UserId => IsAuthenticated && Guid.TryParse(Principal.GetUserId(), out var id) ? id : null;

    public string? Email => IsAuthenticated ? Principal.GetEmail() : null;

    public bool IsInRole(string role) => Principal.IsInRole(role);

    public void RunAs(ClaimsPrincipal principal) => _override = principal;
}
