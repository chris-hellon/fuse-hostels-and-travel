namespace Fuse.Contracts.Identity;

/// <summary>The one password rule, for every password the site or back office sets: legacy's forms' 6 to 100
/// characters plus legacy's Identity options (a lowercase letter; no digit, uppercase or symbol needed). Identity is
/// configured with the same minimum, so this only gives the error under the field first.</summary>
public static class PasswordRules
{
    public const int MinimumLength = 6;
    public const int MaximumLength = 100;

    public static IRuleBuilderOptions<T, string?> ValidPassword<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Length(MinimumLength, MaximumLength)
            .WithMessage($"The {{PropertyName}} must be at least {MinimumLength} and at max {MaximumLength} characters long.")
            .Matches("[a-z]").WithMessage("Passwords must have at least one lowercase ('a'-'z').");
}
