using Fuse.Contracts.Dashboard;
using Fuse.Contracts.Tours;

namespace Fuse.Contracts.Bookings;

/// <summary>A row of the tour bookings table.</summary>
public sealed record BookingRow
{
    public DefaultIdType Id { get; init; }
    public int Reference { get; init; }
    public string Description { get; init; } = "";
    public string? GuestId { get; init; }
    public string? GuestName { get; init; }
    public bool GuestHasEmail { get; init; }
    public DateTime BookingDate { get; init; }
    public decimal TotalAmount { get; init; }
    /// <summary>What the guest still owes (the whole total on an unpaid booking).</summary>
    public decimal Outstanding { get; init; }
    [NotSortable] public BookingState Status { get; init; }
    public bool IsPaid { get; init; }
    public bool Cancelled { get; init; }
    public bool Refunded { get; init; }
    /// <summary>Cancelled with nothing refunded (on the booking, its tours or its payments): can be un-cancelled.</summary>
    public bool Restorable { get; init; }
    public bool ContainsPackage { get; init; }
    public bool HasPackIncentive { get; init; }

    /// <summary>Can take a payment: not cancelled or refunded, and something is owed.</summary>
    [NotSortable] public bool Payable => !Cancelled && !Refunded && Outstanding > 0;
}

/// <summary>
/// Tour bookings, newest first. Suppliers see bookings that include their tours. Dates are whole
/// days (inclusive); either end of a range can be left open.
/// </summary>
[Post("bookings/search"), Permission(FuseAction.View, FuseResource.TourBookings)]
public sealed record SearchBookings : PagedQuery, IQuery<PagedResult<BookingRow>>
{
    public DefaultIdType? TourId { get; init; }
    public DateTime? BookedFrom { get; init; }
    public DateTime? BookedTo { get; init; }
    public DateTime? DepartsFrom { get; init; }
    public DateTime? DepartsTo { get; init; }
    public string? Source { get; init; }
}

/// <summary>
/// The bookings as a spreadsheet (one row per guest per tour, the legacy export columns). At most a
/// month of booking dates and a month of departures: see <see cref="ExportWindow"/>.
/// </summary>
[Post("bookings/export"), Permission(FuseAction.Export, FuseResource.Bookings)]
public sealed record ExportBookings : IQuery<FileDownload>
{
    public string? Keyword { get; init; }
    public DefaultIdType? TourId { get; init; }
    public DateTime? BookedFrom { get; init; }
    public DateTime? BookedTo { get; init; }
    public DateTime? DepartsFrom { get; init; }
    public DateTime? DepartsTo { get; init; }
}

/// <summary>
/// Legacy's export limits: each date range is capped at one month, and with no dates at all the export
/// covers bookings made in the last month. The page shows the notes; the server applies the same rules.
/// </summary>
public static class ExportWindow
{
    public sealed record Result(ExportBookings Export, IReadOnlyList<string> Notes);

    public static Result Normalize(ExportBookings export, DateTime today)
    {
        var notes = new List<string>();
        if (export.BookedFrom is null && export.BookedTo is null && export.DepartsFrom is null && export.DepartsTo is null)
        {
            notes.Add("Export range can't exceed one month: exporting bookings made in the last month.");
            return new(export with { BookedFrom = today.AddMonths(-1), BookedTo = today }, notes);
        }

        var (bookedFrom, bookedTo) = Cap(export.BookedFrom, export.BookedTo, "Booking", notes);
        var (departsFrom, departsTo) = Cap(export.DepartsFrom, export.DepartsTo, "Departure", notes);
        return new(export with { BookedFrom = bookedFrom, BookedTo = bookedTo, DepartsFrom = departsFrom, DepartsTo = departsTo }, notes);
    }

    private static (DateTime?, DateTime?) Cap(DateTime? start, DateTime? end, string label, List<string> notes)
    {
        if (start is null && end is null)
            return (null, null);

        var from = start?.Date ?? end!.Value.Date.AddMonths(-1).AddDays(1);
        var to = end?.Date ?? from.AddMonths(1).AddDays(-1);
        if (to < from)
            (from, to) = (to, from);

        var latest = from.AddMonths(1).AddDays(-1);
        if (to > latest || start is null || end is null)
        {
            to = to > latest ? latest : to;
            notes.Add($"{label} dates limited to one month: {from:d MMM yyyy} to {to:d MMM yyyy}.");
        }

        return (from, to);
    }
}

/// <summary>"Website", "Online", "Booking App" and every property (the legacy booking source list).</summary>
[Get("bookings/sources"), Permission(FuseAction.View, FuseResource.TourBookings)]
public sealed record ListBookingSources : IQuery<IReadOnlyList<string>>;

/// <summary>Tours that can appear on bookings (the bookings filter; suppliers get their own).</summary>
[Get("bookings/tours"), Permission(FuseAction.View, FuseResource.TourBookings)]
public sealed record ListBookingTours : IQuery<IReadOnlyList<LookupItem>>;

/// <summary>What the current user may do to bookings beyond the permission matrix (legacy special cases).</summary>
public sealed record BookingAbilities
{
    /// <summary>Refund and cancel (legacy: Delete on Tour Bookings).</summary>
    public bool CanRefund { get; init; }
    /// <summary>Mark a booking paid without a payment (a configured list of staff, as legacy).</summary>
    public bool CanFlagAsPaid { get; init; }
    /// <summary>Change "Booked By" (legacy: may update staff).</summary>
    public bool CanChangeSalesPerson { get; init; }
    public bool IsAdmin { get; init; }
}

[Get("bookings/abilities"), Permission(FuseAction.View, FuseResource.TourBookings)]
public sealed record GetBookingAbilities : IQuery<BookingAbilities>;

// The expanded row ----------------------------------------------------------------------------

/// <summary>A guest on a booked tour (the lead guest first).</summary>
public sealed record BookingGuestView
{
    public string GuestId { get; init; } = "";
    public string? Name { get; init; }
    public DateTime? DateOfBirth { get; init; }
    public string? Nationality { get; init; }
    public string? Gender { get; init; }
    public string? Email { get; init; }
    public BookingState Status { get; init; }
}

public sealed record BookingItemView
{
    public DefaultIdType Id { get; init; }
    public string TourName { get; init; } = "";
    public string? PackageName { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public string? PickupLocation { get; init; }
    /// <summary>Per guest.</summary>
    public decimal Price { get; init; }
    public int GuestCount { get; init; }
    public decimal Total => Price * GuestCount;
    public BookingState Status { get; init; }
    public bool WaiverSigned { get; init; }
    public bool Cancelled { get; init; }
    public bool Refunded { get; init; }
    public IReadOnlyList<BookingGuestView> Guests { get; init; } = [];
}

public sealed record BookingPaymentView
{
    public decimal Amount { get; init; }
    public DateTime? PaymentDate { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public BookingPaymentMethod Method { get; init; }
    public bool Refunded { get; init; }
}

/// <summary>Everything the bookings table shows when a row is expanded (legacy "Show Details").</summary>
public sealed record BookingOverview
{
    public DefaultIdType Id { get; init; }
    public string? AdditionalNotes { get; init; }
    public decimal? AmountOutstanding { get; init; }
    /// <summary>The staff member who made the booking; null when the guest booked on the website.</summary>
    public string? BookedBy { get; init; }
    public string? BookingSource { get; init; }
    public string? GuestId { get; init; }
    public string? GuestName { get; init; }
    /// <summary>The lead guest's date of birth, gender, nationality and email (legacy "Primary Guest" line).</summary>
    public string? GuestDetail { get; init; }
    public IReadOnlyList<BookingItemView> Items { get; init; } = [];
    public IReadOnlyList<BookingPaymentView> Payments { get; init; } = [];
}

[Get("bookings/{Id}/overview"), Permission(FuseAction.View, FuseResource.TourBookings)]
public sealed record GetBookingOverview(DefaultIdType Id) : IQuery<BookingOverview>;

// Payments, cancellations and refunds -----------------------------------------------------------
// Cancel and refund answer with a message for the page to show (e.g. "refunded $20 through Stripe").

/// <summary>Records money received outside Stripe checkout (cash, or a Stripe payment taken elsewhere).</summary>
[Post("bookings/{BookingId}/payments"), Permission(FuseAction.Update, FuseResource.Bookings)]
public sealed record AddBookingPayment(DefaultIdType BookingId, decimal Amount, BookingPaymentMethod Method, string? StripePaymentId) : ICommand;

/// <summary>Marks a booking paid in full without recording a payment (limited staff, see <see cref="BookingAbilities.CanFlagAsPaid"/>).</summary>
[Put("bookings/{Id}/paid"), Permission(FuseAction.Update, FuseResource.Bookings)]
public sealed record FlagBookingAsPaid(DefaultIdType Id) : ICommand;

/// <summary>Deletes an unpaid booking (or a cancelled one, for staff who can flag bookings paid) and frees its spaces.</summary>
[Delete("bookings/{Id}"), Permission(FuseAction.Delete, FuseResource.Bookings)]
public sealed record DeleteBooking(DefaultIdType Id) : ICommand;

/// <summary>Cancels the whole booking, freeing its spaces, after refunding <see cref="RefundAmount"/> (0 = no refund).</summary>
[Put("bookings/{Id}/cancel"), Permission(FuseAction.Delete, FuseResource.TourBookings)]
public sealed record CancelBooking(DefaultIdType Id, decimal RefundAmount) : ICommand<string>;

/// <summary>
/// Un-cancels a cancelled booking that hasn't been refunded: it and all its tours and guests are live again, retaking
/// their spaces (refused if a departure hasn't enough left).
/// </summary>
[Put("bookings/{Id}/uncancel"), Permission(FuseAction.Delete, FuseResource.TourBookings)]
public sealed record UncancelBooking(DefaultIdType Id) : ICommand;

/// <summary>Refunds part or all of a paid booking through Stripe.</summary>
[Put("bookings/{Id}/refund"), Permission(FuseAction.Delete, FuseResource.TourBookings)]
public sealed record RefundBooking(DefaultIdType Id, decimal Amount) : ICommand<string>;

/// <summary>Cancels one tour of a booking (freeing its spaces) after refunding <see cref="RefundAmount"/>.</summary>
[Put("bookings/items/{ItemId}/cancel"), Permission(FuseAction.Delete, FuseResource.TourBookings)]
public sealed record CancelBookingItem(DefaultIdType ItemId, decimal RefundAmount) : ICommand<string>;

/// <summary>Refunds part or all of one tour of a booking through Stripe.</summary>
[Put("bookings/items/{ItemId}/refund"), Permission(FuseAction.Delete, FuseResource.TourBookings)]
public sealed record RefundBookingItem(DefaultIdType ItemId, decimal Amount) : ICommand<string>;

/// <summary>
/// Refunds one payment (legacy Reports › Payments): through that payment's own Stripe checkout, all of what it charged
/// (<see cref="Amount"/> is the payment's amount) or part of it.
/// </summary>
[Put("bookings/payments/{PaymentId}/refund"), Permission(FuseAction.Delete, FuseResource.TourBookings)]
public sealed record RefundBookingPayment(DefaultIdType PaymentId, decimal Amount) : ICommand<string>;

public sealed class AddBookingPaymentValidator : AbstractValidator<AddBookingPayment>
{
    public AddBookingPaymentValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("The payment amount must be greater than zero.");
        RuleFor(x => x.StripePaymentId).NotEmpty().When(x => x.Method == BookingPaymentMethod.Stripe)
            .WithMessage("A Stripe payment ID is required for Stripe payments.");
        RuleFor(x => x.StripePaymentId).MaximumLength(450);
    }
}

public sealed class CancelBookingValidator : AbstractValidator<CancelBooking>
{
    public CancelBookingValidator() => RuleFor(x => x.RefundAmount).GreaterThanOrEqualTo(0);
}

public sealed class RefundBookingValidator : AbstractValidator<RefundBooking>
{
    public RefundBookingValidator() => RuleFor(x => x.Amount).GreaterThan(0)
        .WithMessage("Enter an amount greater than zero to refund. To give nothing back, cancel instead.");
}

public sealed class RefundBookingPaymentValidator : AbstractValidator<RefundBookingPayment>
{
    public RefundBookingPaymentValidator() => RuleFor(x => x.Amount).GreaterThan(0)
        .WithMessage("You must enter a value greater than zero to process a refund. Try cancelling the booking instead.");
}

public sealed class CancelBookingItemValidator : AbstractValidator<CancelBookingItem>
{
    public CancelBookingItemValidator() => RuleFor(x => x.RefundAmount).GreaterThanOrEqualTo(0);
}

public sealed class RefundBookingItemValidator : AbstractValidator<RefundBookingItem>
{
    public RefundBookingItemValidator() => RuleFor(x => x.Amount).GreaterThan(0)
        .WithMessage("Enter an amount greater than zero to refund. To give nothing back, cancel instead.");
}
