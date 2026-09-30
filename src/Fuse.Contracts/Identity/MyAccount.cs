namespace Fuse.Contracts.Identity;

// The public site's My Account (legacy Identity/Account/Manage): a guest's own bookings, paying what they owe, their
// email (changed only through a link sent to the new address), a first password, and a copy of their data.

public enum MyBookingStatus
{
    /// <summary>Paid in full.</summary>
    Paid,
    /// <summary>Something's owed; the guest can pay it here.</summary>
    Due,
    /// <summary>A website checkout still on Stripe's page (paid there, or released if not).</summary>
    AwaitingPayment,
    Cancelled,
    Refunded,
}

public sealed record MyBookingSummary
{
    public DefaultIdType Id { get; init; }
    public int Reference { get; init; }
    public DateTime BookedOn { get; init; }
    public MyBookingStatus Status { get; init; }
    public decimal Total { get; init; }
    public decimal Outstanding { get; init; }
    /// <summary>The first tour's name, and how many more there are.</summary>
    public string Title { get; init; } = "";
    public int MoreTours { get; init; }
    public string? ImagePath { get; init; }
    /// <summary>When its first tour starts (null when it has none).</summary>
    public DateTime? Starts { get; init; }
    /// <summary>Its last tour hasn't started yet.</summary>
    public bool Upcoming { get; init; }
}

[Get("me/bookings"), Authenticated]
public sealed record ListMyBookings : IQuery<IReadOnlyList<MyBookingSummary>>;

public sealed record MyBookingTour
{
    public string Name { get; init; } = "";
    /// <summary>Its page on the site, when it's still published.</summary>
    public string? Slug { get; init; }
    public string? ImagePath { get; init; }
    public DateTime Start { get; init; }
    public DateTime? End { get; init; }
    /// <summary>The booker plus everyone added.</summary>
    public int Guests { get; init; }
    public IReadOnlyList<string> GuestNames { get; init; } = [];
    public string? Pickup { get; init; }
    /// <summary>Per guest.</summary>
    public decimal Price { get; init; }
    public bool Cancelled { get; init; }
    public decimal Total => Price * Guests;
}

public sealed record MyBookingPayment(DateTime PaidOn, decimal Amount, string? Method);

public sealed record MyBooking
{
    public DefaultIdType Id { get; init; }
    public int Reference { get; init; }
    public DateTime BookedOn { get; init; }
    public MyBookingStatus Status { get; init; }
    public decimal Total { get; init; }
    public decimal Outstanding { get; init; }
    public decimal Paid => Math.Max(0, Total - Outstanding);
    public string? Notes { get; init; }
    public IReadOnlyList<MyBookingTour> Tours { get; init; } = [];
    public IReadOnlyList<MyBookingPayment> Payments { get; init; } = [];
}

[Get("me/bookings/{Id}"), Authenticated]
public sealed record GetMyBooking(DefaultIdType Id) : IQuery<MyBooking>;

/// <summary>Stripe's page for what the booking owes (returns its address).</summary>
[Post("me/bookings/{Id}/payment"), Authenticated]
public sealed record PayMyBooking(DefaultIdType Id) : ICommand<string>;

/// <summary>Emails a confirmation link to the new address; the email changes only when it's followed.</summary>
[Post("me/email"), Authenticated, RateLimited]
public sealed record RequestMyEmailChange(string NewEmail) : ICommand;

/// <summary>The link from <see cref="RequestMyEmailChange"/> (works signed in or not: the token is the proof).</summary>
[Post("me/email/confirmation"), Public]
public sealed record ConfirmMyEmailChange(string UserId, string Email, string Token) : ICommand;

/// <summary>A first password, for an account made at checkout without one.</summary>
[Post("me/password"), Authenticated]
public sealed record SetMyPassword(string NewPassword, string ConfirmNewPassword) : ICommand;

/// <summary>Everything the account holds about the guest (their profile and bookings), for them to download.</summary>
[Get("me/data"), Authenticated]
public sealed record GetMyData : IQuery<MyData>;

public sealed record MyData(UserProfile Profile, DateTime? SignedUp, IReadOnlyList<MyBooking> Bookings);

public sealed class RequestMyEmailChangeValidator : AbstractValidator<RequestMyEmailChange>
{
    public RequestMyEmailChangeValidator() =>
        RuleFor(x => x.NewEmail).NotEmpty().WithMessage("Please enter your new email address.").EmailAddress().MaximumLength(256).WithName("New email");
}

public sealed class SetMyPasswordValidator : AbstractValidator<SetMyPassword>
{
    public SetMyPasswordValidator()
    {
        RuleFor(x => x.NewPassword).NotEmpty().ValidPassword().WithName("New password");
        RuleFor(x => x.ConfirmNewPassword).Equal(x => x.NewPassword).WithMessage("Passwords do not match.");
    }
}
