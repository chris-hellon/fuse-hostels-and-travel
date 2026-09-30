namespace Fuse.Contracts.Identity;

/// <summary>A row in the staff or guest tables.</summary>
public sealed record UserSummary
{
    public string Id { get; init; } = "";
    public string? FullName { get; init; }
    public string? UserName { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public bool IsActive { get; init; }
    public DateTime? DateOfBirth { get; init; }
    public string? Gender { get; init; }
    public string? Nationality { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
}

/// <summary>Everything editable on a person (staff member or guest).</summary>
public sealed record UserProfile
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Nationality { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? ZipPostalCode { get; set; }
    public string? PassportNumber { get; set; }
    public DateTime? PassportIssueDate { get; set; }
    public DateTime? PassportExpiryDate { get; set; }
    public string? PassportIssuingCountry { get; set; }
    public DateTime? VisaIssueDate { get; set; }
    public DateTime? VisaExpiryDate { get; set; }
}

public sealed record UserDetails
{
    public string Id { get; init; } = "";
    public string? UserName { get; init; }
    public string? FullName { get; init; }
    public string? ImageUrl { get; init; }
    public bool IsActive { get; init; }
    public DateTime? SignUpDate { get; init; }
    public string? CloudbedsGuestId { get; init; }
    /// <summary>False for a guest account made at checkout without a password.</summary>
    public bool HasPassword { get; init; }
    public UserProfile Profile { get; init; } = new();
}

// Staff -------------------------------------------------------------------------------------

[Post("staff/search"), Permission(FuseAction.View, FuseResource.Users)]
public sealed record SearchStaff : PagedQuery, IQuery<PagedResult<UserSummary>>;

[Post("staff"), Permission(FuseAction.Create, FuseResource.Users)]
public sealed record CreateStaff(UserProfile Profile, string Password, string ConfirmPassword) : ICommand<string>;

[Get("staff/{Id}"), Permission(FuseAction.View, FuseResource.Users)]
public sealed record GetStaff(string Id) : IQuery<UserDetails>;

[Put("staff/{Id}"), Permission(FuseAction.Update, FuseResource.Users)]
public sealed record UpdateStaff(string Id, UserProfile Profile) : ICommand;

[Put("staff/{Id}/active"), Permission(FuseAction.Update, FuseResource.Users)]
public sealed record SetStaffActive(string Id, bool IsActive) : ICommand;

/// <summary>Uploads (or replaces) the staff member's profile photo, or removes it when <see cref="Image"/> is null
/// (legacy's profile Upload / Delete). Returns the new <see cref="UserDetails.ImageUrl"/>, empty once removed.</summary>
[Put("staff/{Id}/image"), Permission(FuseAction.Update, FuseResource.Users)]
public sealed record SetStaffImage(string Id, FileUpload? Image) : ICommand<string>;

/// <summary>An administrator sets a staff member's (or supplier's) password, e.g. when they've forgotten it.</summary>
[Put("staff/{Id}/password"), Permission(FuseAction.Update, FuseResource.Users)]
public sealed record SetStaffPassword(string Id, string Password, string ConfirmPassword) : ICommand;

/// <summary>Emails the staff member a back-office link to choose a new password (the Staff list's "Send Password Reset
/// Link", available to anyone who can see the list, as legacy).</summary>
[Post("staff/{Id}/password-reset"), Permission(FuseAction.View, FuseResource.Users)]
public sealed record SendStaffPasswordReset(string Id) : ICommand;

// Suppliers (staff accounts with the Supplier role) -----------------------------------------

[Post("suppliers/search"), Permission(FuseAction.View, FuseResource.Suppliers)]
public sealed record SearchSuppliers : PagedQuery, IQuery<PagedResult<UserSummary>>;

/// <summary>A supplier signs in to the back office to see their own tours' bookings.</summary>
[Post("suppliers"), Permission(FuseAction.Create, FuseResource.Suppliers)]
public sealed record CreateSupplier(UserProfile Profile, string Password, string ConfirmPassword) : ICommand<string>;

/// <summary>The Suppliers list's "Send Password Reset Link" (as <see cref="SendStaffPasswordReset"/>, for suppliers).</summary>
[Post("suppliers/{Id}/password-reset"), Permission(FuseAction.View, FuseResource.Suppliers)]
public sealed record SendSupplierPasswordReset(string Id) : ICommand;

// Guests ------------------------------------------------------------------------------------

[Post("guests/search"), Permission(FuseAction.View, FuseResource.Guests)]
public sealed record SearchGuests : PagedQuery, IQuery<PagedResult<UserSummary>>;

/// <summary>Type-ahead guest picker (bookings, guest lists). Prefix match on name/email.</summary>
[Get("guests/find"), Permission(FuseAction.Search, FuseResource.Guests)]
public sealed record FindGuests(string Search) : IQuery<IReadOnlyList<UserSummary>>;

[Post("guests"), Permission(FuseAction.Create, FuseResource.Guests)]
public sealed record CreateGuest(UserProfile Profile) : ICommand<string>;

[Get("guests/{Id}"), Permission(FuseAction.View, FuseResource.Guests)]
public sealed record GetGuest(string Id) : IQuery<UserDetails>;

[Put("guests/{Id}"), Permission(FuseAction.Update, FuseResource.Guests)]
public sealed record UpdateGuest(string Id, UserProfile Profile) : ICommand;

/// <summary>
/// The guest's details as Cloudbeds holds them, mapped to our fields, so the Edit Guest dialog can offer
/// each differing value. Read-only: nothing is written to Cloudbeds.
/// </summary>
[Get("guests/{Id}/cloudbeds"), Permission(FuseAction.View, FuseResource.Guests)]
public sealed record GetGuestCloudbedsProfile(string Id) : IQuery<CloudbedsGuestLookup>;

/// <param name="CloudbedsGuestId">Null when the guest isn't linked to Cloudbeds.</param>
/// <param name="Profile">Null when linked but no property's Cloudbeds account could return the guest.</param>
public sealed record CloudbedsGuestLookup(string? CloudbedsGuestId, UserProfile? Profile);

// Validation (pure rules; uniqueness checks run server-side) ---------------------------------

public sealed class UserProfileValidator : AbstractValidator<UserProfile>
{
    public UserProfileValidator()
    {
        RuleFor(p => p.FirstName).NotEmpty().MaximumLength(75).WithName("First Name");
        RuleFor(p => p.LastName).NotEmpty().MaximumLength(75).WithName("Last Name");
        RuleFor(p => p.Email).EmailAddress().MaximumLength(256).When(p => !string.IsNullOrWhiteSpace(p.Email));
        RuleFor(p => p.PhoneNumber).MaximumLength(50).WithName("Phone Number");
        RuleFor(p => p.DateOfBirth).LessThan(_ => DateTime.Today).When(p => p.DateOfBirth.HasValue)
            .WithMessage("Date Of Birth must be in the past.");
    }
}

public sealed class CreateStaffValidator : AbstractValidator<CreateStaff>
{
    public CreateStaffValidator()
    {
        RuleFor(x => x.Profile).SetValidator(new UserProfileValidator());
        RuleFor(x => x.Profile.Email).NotEmpty().WithName("Email");
        RuleFor(x => x.Password).NotEmpty().ValidPassword();
        RuleFor(x => x.ConfirmPassword).Equal(x => x.Password).WithMessage("Passwords do not match.");
    }
}

public sealed class CreateSupplierValidator : AbstractValidator<CreateSupplier>
{
    public CreateSupplierValidator()
    {
        RuleFor(x => x.Profile).SetValidator(new UserProfileValidator());
        RuleFor(x => x.Profile.Email).NotEmpty().WithName("Email");
        RuleFor(x => x.Password).NotEmpty().ValidPassword();
        RuleFor(x => x.ConfirmPassword).Equal(x => x.Password).WithMessage("Passwords do not match.");
    }
}

public sealed class SetStaffPasswordValidator : AbstractValidator<SetStaffPassword>
{
    public SetStaffPasswordValidator()
    {
        RuleFor(x => x.Password).NotEmpty().ValidPassword().WithName("New password");
        RuleFor(x => x.ConfirmPassword).Equal(x => x.Password).WithName("Confirm password").WithMessage("Passwords do not match.");
    }
}

public sealed class UpdateStaffValidator : AbstractValidator<UpdateStaff>
{
    public UpdateStaffValidator()
    {
        RuleFor(x => x.Profile).SetValidator(new UserProfileValidator());
        RuleFor(x => x.Profile.Email).NotEmpty().WithName("Email");
    }
}

public sealed class CreateGuestValidator : AbstractValidator<CreateGuest>
{
    public CreateGuestValidator() => RuleFor(x => x.Profile).SetValidator(new UserProfileValidator());
}

public sealed class UpdateGuestValidator : AbstractValidator<UpdateGuest>
{
    public UpdateGuestValidator() => RuleFor(x => x.Profile).SetValidator(new UserProfileValidator());
}
