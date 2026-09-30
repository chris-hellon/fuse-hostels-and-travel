using Fuse.Contracts.Identity;

namespace Fuse.Contracts.Auth;

// Sign-in has to set a cookie on a real HTTP response, so these are plain /api/auth endpoints
// rather than Mediator contracts (which can also run in-process during prerendering).

public static class AuthRoutes
{
    public const string SignIn = "api/auth/sign-in";
    public const string Register = "api/auth/register";
    public const string SignOut = "api/auth/sign-out";
    public const string Me = "api/auth/me";
}

public sealed record SignInRequest
{
    public string Email { get; init; } = "";
    public string Password { get; init; } = "";

    /// <summary>Back-office sign-in: refuse guests and inactive staff.</summary>
    public bool StaffOnly { get; init; }
}

public sealed record RegisterGuestRequest
{
    public string FirstName { get; init; } = "";
    public string Surname { get; init; } = "";
    public string Email { get; init; } = "";
    public DateTime? DateOfBirth { get; init; }
    public string PhoneNumber { get; init; } = "";
    public string Nationality { get; init; } = "";
    public string Gender { get; init; } = "";
    public string Password { get; init; } = "";
    public string ConfirmPassword { get; init; } = "";

    /// <summary>Left empty by people, filled in by bots.</summary>
    public string? HoneyPot { get; init; }
}

public sealed record ClaimValue(string Type, string Value);

/// <summary>The signed-in user as the browser sees it; rebuilt into a ClaimsPrincipal client-side.</summary>
public sealed record CurrentUserInfo(bool IsAuthenticated, IReadOnlyList<ClaimValue> Claims)
{
    public static CurrentUserInfo Anonymous { get; } = new(false, []);
}

public sealed class SignInRequestValidator : AbstractValidator<SignInRequest>
{
    public SignInRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithName("Email");
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class RegisterGuestRequestValidator : AbstractValidator<RegisterGuestRequest>
{
    public RegisterGuestRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100).WithName("First Name");
        RuleFor(x => x.Surname).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.DateOfBirth).NotNull().WithName("Date Of Birth")
            .LessThan(_ => DateTime.Today).WithMessage("Date Of Birth must be in the past.");
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(50).WithName("Phone Number");
        RuleFor(x => x.Nationality).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Gender).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Password).NotEmpty().ValidPassword();
        RuleFor(x => x.ConfirmPassword).Equal(x => x.Password)
            .WithMessage("The password and confirmation password do not match.").WithName("Confirm password");
    }
}
