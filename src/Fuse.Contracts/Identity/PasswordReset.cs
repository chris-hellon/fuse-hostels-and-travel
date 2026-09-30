namespace Fuse.Contracts.Identity;

// Forgot password, for guests (the website's sign-in) and staff (the back office's). Asking always "works", so the
// form never tells anyone which emails have accounts; an account gets a link (24 hours, single use) to choose a new
// password. It's also how a guest whose account was made at checkout, without a password, first sets one.

/// <summary>Emails a reset link if <see cref="Email"/> has an account; <see cref="Staff"/> sends a back-office link,
/// and only to staff.</summary>
[Post("password-reset"), Public, RateLimited]
public sealed record RequestPasswordReset(string Email, bool Staff = false) : ICommand;

/// <summary>The link from <see cref="RequestPasswordReset"/>: the new password (the token is the proof).</summary>
[Post("password-reset/confirmation"), Public, RateLimited]
public sealed record ResetPassword(string UserId, string Token, string NewPassword, string ConfirmNewPassword) : ICommand;

public sealed class RequestPasswordResetValidator : AbstractValidator<RequestPasswordReset>
{
    public RequestPasswordResetValidator() =>
        RuleFor(x => x.Email).NotEmpty().WithMessage("Please enter your email address.").EmailAddress().MaximumLength(256);
}

public sealed class ResetPasswordValidator : AbstractValidator<ResetPassword>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().ValidPassword().WithName("New password");
        RuleFor(x => x.ConfirmNewPassword).Equal(x => x.NewPassword).WithMessage("Passwords do not match.");
    }
}
