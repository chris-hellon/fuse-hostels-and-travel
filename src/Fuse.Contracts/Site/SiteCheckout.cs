using Fuse.Contracts.Identity;

namespace Fuse.Contracts.Site;

// The website's basket and checkout (legacy /basket, /checkout): the basket lives in the browser; these price it
// on the server and turn it into a booking paid through Stripe's hosted checkout.

/// <summary>A departure in the visitor's basket: how many guests (the booker included) and where they're picked up.</summary>
public sealed record BasketLineRequest(DefaultIdType DepartureId, int Guests, string? Pickup);

/// <summary>A basket line as it stands now: current price, and a problem if it can't be booked as it is.</summary>
public sealed record BasketQuoteLine
{
    public DefaultIdType DepartureId { get; init; }
    public DefaultIdType TourId { get; init; }
    public string TourName { get; init; } = "";
    public string TourSlug { get; init; } = "";
    public string? ImagePath { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    public string PriceTitle { get; init; } = "";
    public decimal UnitPrice { get; init; }
    public int Guests { get; init; }
    public string? Pickup { get; init; }
    /// <summary>The tour's pick-up points (choose one when there are several).</summary>
    public IReadOnlyList<string> Pickups { get; init; } = [];
    /// <summary>"Sold out.", "Only 2 places left.", …; null when it can be booked.</summary>
    public string? Problem { get; init; }
    public decimal Total => UnitPrice * Guests;
}

public sealed record BasketQuote(IReadOnlyList<BasketQuoteLine> Lines)
{
    public decimal Total => Lines.Where(l => l.Problem is null).Sum(l => l.Total);
    public bool Bookable => Lines.Count > 0 && Lines.All(l => l.Problem is null);
}

/// <summary>Prices the basket and checks each departure can still be booked (never trusts the browser's prices).</summary>
[Post("site/basket/quote"), Public]
public sealed record QuoteBasket(IReadOnlyList<BasketLineRequest> Lines, DefaultIdType? StaffId = null) : IQuery<BasketQuote>;

/// <summary>The lead booker's details (legacy's checkout form: every field required).</summary>
public sealed class CheckoutContact
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public DateTime? DateOfBirth { get; set; }
    public string Gender { get; set; } = "";
    public string Nationality { get; set; } = "";
}

/// <summary>Another guest; <see cref="Key"/> lets one person join several tours in the basket.</summary>
public sealed class CheckoutGuest
{
    public string Key { get; set; } = Guid.NewGuid().ToString("N");
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Nationality { get; set; }
}

/// <summary>A departure to book, with its other guests (the booker is always on it too).</summary>
public sealed class CheckoutTour
{
    public DefaultIdType DepartureId { get; set; }
    public string? Pickup { get; set; }
    public List<string> GuestKeys { get; set; } = [];
}

/// <summary>
/// Books the basket and returns Stripe's payment page. Signed-in visitors book as themselves; otherwise the booking
/// goes to the account with that email (never signed in or changed) or a new guest account (with a password only
/// when they ask for an account). <see cref="Replaces"/>: this visitor's earlier, unpaid attempt (they came back from
/// Stripe without paying), which is withdrawn so it stops holding places.
/// </summary>
[Post("site/checkout"), Public]
public sealed record PlaceBooking(
    CheckoutContact Contact,
    IReadOnlyList<CheckoutGuest> Guests,
    IReadOnlyList<CheckoutTour> Lines,
    string? Password,
    string? ConfirmPassword,
    string? Notes,
    DefaultIdType? Replaces = null,
    DefaultIdType? StaffId = null) : ICommand<PlacedBooking>;

public sealed record PlacedBooking(DefaultIdType BookingId, string PaymentUrl);

public sealed class PlaceBookingValidator : AbstractValidator<PlaceBooking>
{
    public const int MaxLines = 20;

    public PlaceBookingValidator()
    {
        RuleFor(x => x.Contact.FirstName).NotEmpty().MaximumLength(75).WithName("First Name");
        RuleFor(x => x.Contact.LastName).NotEmpty().MaximumLength(75).WithName("Surname");
        RuleFor(x => x.Contact.Email).NotEmpty().EmailAddress().MaximumLength(256).WithName("Email");
        RuleFor(x => x.Contact.PhoneNumber).NotEmpty().MaximumLength(50).WithName("Phone Number");
        RuleFor(x => x.Contact.DateOfBirth).NotNull().LessThan(_ => DateTime.Today).WithName("Date Of Birth");
        RuleFor(x => x.Contact.Gender).NotEmpty().WithName("Gender");
        RuleFor(x => x.Contact.Nationality).NotEmpty().WithName("Nationality");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Your basket is empty.")
            .Must(lines => lines.Count <= MaxLines).WithMessage($"A booking can have at most {MaxLines} tours.");
        RuleForEach(x => x.Guests).ChildRules(guest =>
        {
            guest.RuleFor(g => g.FirstName).NotEmpty().MaximumLength(75).WithName("First Name");
            guest.RuleFor(g => g.LastName).NotEmpty().MaximumLength(75).WithName("Surname");
            guest.RuleFor(g => g.Email).EmailAddress().MaximumLength(256).When(g => !string.IsNullOrWhiteSpace(g.Email));
        });
        RuleFor(x => x).Must(x => x.Lines.All(l => l.GuestKeys.All(k => x.Guests.Any(g => g.Key == k))))
            .WithName("Guests").WithMessage("A guest on a tour is missing their details.");
        RuleFor(x => x.Password).ValidPassword().When(x => !string.IsNullOrEmpty(x.Password));
        RuleFor(x => x.ConfirmPassword).Equal(x => x.Password).When(x => !string.IsNullOrEmpty(x.Password))
            .WithMessage("The password and confirmation password do not match.");
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public sealed class QuoteBasketValidator : AbstractValidator<QuoteBasket>
{
    public QuoteBasketValidator()
    {
        RuleFor(x => x.Lines).Must(lines => lines.Count <= PlaceBookingValidator.MaxLines);
        RuleForEach(x => x.Lines).ChildRules(line => line.RuleFor(l => l.Guests).InclusiveBetween(1, 50));
    }
}
