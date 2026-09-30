#if DEBUG
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Fuse.Domain.Identity;
using Fuse.Shared.Authorization;
using Fuse.Shared.Multitenancy;

namespace Fuse.Web.Auth;

/// <summary>
/// Development only: <c>/dev/sign-in</c> signs the browser in as a local admin with no password, so the
/// back office can be checked in a browser without handling credentials. It exists only in Debug
/// builds, in the Development environment, against the local database, and for requests from this
/// machine; anywhere else the route isn't mapped (or answers 404).
/// </summary>
public static class DevSignIn
{
    public const string Email = "dev-admin@localhost.invalid";

    public static void MapDevSignIn(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment() || app.Configuration.GetValue("Database:AllowNonLocalServer", false))
            return;

        app.MapGet("/dev/sign-in", SignInAsync).ExcludeFromDescription().AllowAnonymous();
    }

    private static async Task<IResult> SignInAsync(
        HttpContext context,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        string? returnUrl)
    {
        // This machine only: a loopback connection addressed to localhost, not relayed by a proxy or tunnel (ngrok
        // connects from this machine too, so the connection alone would let anyone through it sign in as admin).
        if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip)
            || !context.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || context.Request.Headers.ContainsKey("X-Forwarded-For") || context.Request.Headers.ContainsKey("X-Forwarded-Host"))
            return Results.NotFound();

        var user = await users.FindByEmailAsync(Email);
        if (user is null)
        {
            // No password: this account can only ever be used through this route.
            user = new ApplicationUser
            {
                UserName = Email, Email = Email, EmailConfirmed = true, IsActive = true,
                FirstName = "Dev", LastName = "Admin", FullName = "Dev Admin",
                SignUpDate = DateTime.UtcNow, RefreshTokenExpiryTime = DateTime.UtcNow,
            };
            var created = await users.CreateAsync(user);
            if (!created.Succeeded)
                return Results.Problem(string.Join(" ", created.Errors.Select(e => e.Description)));
        }
        if (!await users.IsInRoleAsync(user, FuseRoles.Admin))
            await users.AddToRoleAsync(user, FuseRoles.Admin);

        await signIn.SignInWithClaimsAsync(user, isPersistent: false,
            [new Claim(FuseClaims.Tenant, MultitenancyConstants.Fuse.Identifier)]);

        return Results.LocalRedirect(returnUrl is { Length: > 0 } && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") ? returnUrl : "/admin");
    }
}
#endif
