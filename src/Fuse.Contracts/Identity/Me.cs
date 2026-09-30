namespace Fuse.Contracts.Identity;

// The signed-in user's own account: back office "My account" and the public site's My Account.

[Get("me/profile"), Authenticated]
public sealed record GetMyProfile : IQuery<UserDetails>;

[Put("me/profile"), Authenticated]
public sealed record UpdateMyProfile(UserProfile Profile) : ICommand;

/// <summary>The back office's own profile photo (as <see cref="SetStaffImage"/>): null removes it.</summary>
[Put("me/image"), Staff]
public sealed record SetMyImage(FileUpload? Image) : ICommand<string>;

[Put("me/password"), Authenticated]
public sealed record ChangeMyPassword(string CurrentPassword, string NewPassword, string ConfirmNewPassword) : ICommand;

public sealed class UpdateMyProfileValidator : AbstractValidator<UpdateMyProfile>
{
    public UpdateMyProfileValidator()
    {
        RuleFor(x => x.Profile).SetValidator(new UserProfileValidator());
        RuleFor(x => x.Profile.Email).NotEmpty().WithName("Email");
    }
}

public sealed class ChangeMyPasswordValidator : AbstractValidator<ChangeMyPassword>
{
    public ChangeMyPasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithName("Current password");
        RuleFor(x => x.NewPassword).NotEmpty().ValidPassword().WithName("New password");
        RuleFor(x => x.ConfirmNewPassword).Equal(x => x.NewPassword).WithMessage("Passwords do not match.");
    }
}
