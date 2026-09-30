using System.Security.Claims;

namespace Fuse.Shared.Authorization;

public static class ClaimsPrincipalExtensions
{
    public static string? GetUserId(this ClaimsPrincipal principal) => principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    public static string? GetEmail(this ClaimsPrincipal principal) => principal.FindFirst(ClaimTypes.Email)?.Value;
    public static string? GetFullName(this ClaimsPrincipal principal) => principal.FindFirst(FuseClaims.Fullname)?.Value;
    public static string? GetFirstName(this ClaimsPrincipal principal) => principal.FindFirst(ClaimTypes.Name)?.Value;
    public static string? GetSurname(this ClaimsPrincipal principal) => principal.FindFirst(ClaimTypes.Surname)?.Value;
    public static string? GetPhoneNumber(this ClaimsPrincipal principal) => principal.FindFirst(ClaimTypes.MobilePhone)?.Value;
    public static string? GetImageUrl(this ClaimsPrincipal principal) => principal.FindFirst(FuseClaims.ImageUrl)?.Value;

    public static bool HasPermission(this ClaimsPrincipal principal, string action, string resource) =>
        principal.HasClaim(FuseClaims.Permission, FusePermission.NameFor(action, resource));

    public static bool IsStaff(this ClaimsPrincipal principal) =>
        FuseRoles.StaffRoles.Any(principal.IsInRole);
}
