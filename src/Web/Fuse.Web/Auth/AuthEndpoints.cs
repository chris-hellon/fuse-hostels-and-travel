using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Fuse.Contracts.Api;
using Fuse.Contracts.Auth;
using Fuse.Contracts.Common;
using Fuse.Domain.Identity;
using Fuse.Shared.Authorization;
using Fuse.Shared.Multitenancy;

namespace Fuse.Web.Auth;

public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";

    // One message for "no such user" and "wrong password": telling them apart lets anyone probe
    // which email addresses have accounts.
    private const string InvalidCredentials = "Your email or password is incorrect. Please check your details, or reset your password.";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(AuthRoutes.SignIn, SignIn).RequireRateLimiting(RateLimitPolicy).AddEndpointFilter<RequireApiMarker>();
        app.MapPost(AuthRoutes.Register, Register).RequireRateLimiting(RateLimitPolicy).AddEndpointFilter<RequireApiMarker>();
        app.MapPost(AuthRoutes.SignOut, SignOut).AddEndpointFilter<RequireApiMarker>();
        app.MapGet(AuthRoutes.Me, (ClaimsPrincipal user) => Describe(user));
        return app;
    }

    private static async Task<IResult> SignIn(
        SignInRequest request,
        IValidator<SignInRequest> validator,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        IUserClaimsPrincipalFactory<ApplicationUser> principals,
        ILogger<SignInRequest> logger)
    {
        await validator.ValidateAndThrowAppAsync(request);

        // The back office signs in by username, as legacy did (staff whose email was changed later still sign in with
        // the address they started with), then by current email. The website signs in by email, as legacy's did.
        var user = request.StaffOnly
            ? await users.FindByNameAsync(request.Email) ?? await users.FindByEmailAsync(request.Email)
            : await users.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive && request.StaffOnly)
            throw new BusinessRuleException(InvalidCredentials);

        if (request.StaffOnly && !(await users.GetRolesAsync(user)).Any(FuseRoles.StaffRoles.Contains))
            throw new ForbiddenException("This account doesn't have access to Fuse.");

        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
            throw new BusinessRuleException("Too many failed attempts. Please try again in a few minutes, or reset your password.");
        if (!result.Succeeded)
            throw new BusinessRuleException(InvalidCredentials);

        // Session cookie (no "remember me"): the legacy site never honoured it, and shared hostel
        // computers are common.
        await signIn.SignInWithClaimsAsync(user, isPersistent: false,
            [new Claim(FuseClaims.Tenant, MultitenancyConstants.Fuse.Identifier)]);
        logger.LogInformation("User {UserId} signed in", user.Id);

        return Describe(await principals.CreateAsync(user));
    }

    private static async Task<IResult> Register(
        RegisterGuestRequest request,
        IValidator<RegisterGuestRequest> validator,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        IUserClaimsPrincipalFactory<ApplicationUser> principals,
        ILogger<RegisterGuestRequest> logger)
    {
        // Bots fill every field; pretend it worked and do nothing.
        if (!string.IsNullOrEmpty(request.HoneyPot))
            return Results.Ok(CurrentUserInfo.Anonymous);

        await validator.ValidateAndThrowAppAsync(request);

        if (await users.FindByEmailAsync(request.Email) is not null)
            throw new ConflictException("A user with this Email Address already exists. Please sign in, or choose a different Email Address.");

        var user = new ApplicationUser
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.Surname.Trim(),
            FullName = PersonNames.Display(request.FirstName, request.Surname),
            PhoneNumber = request.PhoneNumber.Trim(),
            Gender = request.Gender,
            Nationality = request.Nationality,
            DateOfBirth = request.DateOfBirth,
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            SignUpDate = DateTime.UtcNow,
            IsActive = true,
            EmailConfirmed = true,
            RefreshTokenExpiryTime = DateTime.UtcNow,
        };

        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
            throw new RequestValidationException(nameof(RegisterGuestRequest.Password), string.Join(" ", created.Errors.Select(e => e.Description)));

        await users.AddToRoleAsync(user, FuseRoles.Guest);
        await signIn.SignInWithClaimsAsync(user, isPersistent: false,
            [new Claim(FuseClaims.Tenant, MultitenancyConstants.Fuse.Identifier)]);
        logger.LogInformation("Guest {UserId} registered", user.Id);

        return Describe(await principals.CreateAsync(user));
    }

    private static async Task<IResult> SignOut(SignInManager<ApplicationUser> signIn)
    {
        await signIn.SignOutAsync();
        return Results.Ok(CurrentUserInfo.Anonymous);
    }

    private static IResult Describe(ClaimsPrincipal user) =>
        Results.Json(user.Identity?.IsAuthenticated is true
            ? new CurrentUserInfo(true, [.. user.Claims.Select(c => new ClaimValue(c.Type, c.Value))])
            : CurrentUserInfo.Anonymous, ApiJson.Options);

    private static async Task ValidateAndThrowAppAsync<T>(this IValidator<T> validator, T request)
    {
        var result = await validator.ValidateAsync(request);
        if (!result.IsValid)
        {
            throw new RequestValidationException(result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
        }
    }

    private sealed class RequireApiMarker : IEndpointFilter
    {
        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
            context.HttpContext.Request.Headers.ContainsKey(ApiHeaders.Marker)
                ? next(context)
                : ValueTask.FromResult<object?>(Results.Problem(statusCode: 400, title: $"Missing {ApiHeaders.Marker} header."));
    }
}
