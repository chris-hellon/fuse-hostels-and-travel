using Fuse.Application.Common.Files;
using Fuse.Application.Features.Tours;
using Fuse.Contracts.Dashboard;

namespace Fuse.Application.Features.Bookings;

/// <summary>A row of the GetBookingsExports proc: one per guest (lead or additional) on a booked tour.</summary>
internal sealed class BookingExportRow
{
    public int BookingInvoiceId { get; init; }
    public DateTime BookingBookingDate { get; init; }
    public DefaultIdType? TourId { get; init; }
    public string? TourName { get; init; }
    public string? PickupLocation { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public decimal Amount { get; init; }
    public string? PriceLabel { get; init; }
    public int? PaymentMethod { get; init; }
    public bool? BookingRefunded { get; init; }
    public bool? TourRefunded { get; init; }
    public bool BookingIsPaid { get; init; }
    public string? GuestId { get; init; }
    public string? GuestName { get; init; }
    public DateTime? GuestDateOfBirth { get; init; }
    public string? GuestNationality { get; init; }
    public string? GuestGender { get; init; }
    public string? GuestPassportNumber { get; init; }
    public string? BookingAdditionalNotes { get; init; }
    public bool? WaiverSigned { get; init; }
    public string? BookingBookingSource { get; init; }
    public string? BookingStaffName { get; init; }
    public bool? Cancelled { get; init; }
    public bool? NoShow { get; init; }
    public bool? CheckedIn { get; init; }

    /// <summary>As the legacy export's Status column.</summary>
    public BookingState Status => Cancelled == true ? BookingState.Cancelled
        : NoShow == true ? BookingState.NoShow
        : CheckedIn == true ? BookingState.CheckedIn
        : BookingRefunded == true || TourRefunded == true ? BookingState.Refunded
        : BookingIsPaid ? BookingState.Paid
        : BookingState.Unpaid;

    public DepartureRow ToDeparture() => new()
    {
        Reference = BookingInvoiceId,
        TourName = TourName ?? "",
        GuestId = GuestId,
        GuestName = GuestName,
        StartDate = StartDate,
        EndDate = EndDate,
        PickupLocation = PickupLocation,
        WaiverSigned = WaiverSigned == true,
        Status = Status,
    };
}

/// <summary>The bookings export proc, scoped to the tours the current user may see.</summary>
internal static class BookingExports
{
    public sealed record Filter
    {
        public DateTime? TourStart { get; init; }
        public DateTime? TourEnd { get; init; }
        public DateTime? BookingStart { get; init; }
        public DateTime? BookingEnd { get; init; }
        /// <summary>The departures and manifest leave refunds out; the bookings export keeps them.</summary>
        public bool HideRefunded { get; init; } = true;
        public DefaultIdType? TourId { get; init; }
        /// <summary>One TourDates row (price row) of a departure: the tour manifest emails, as legacy.</summary>
        public DefaultIdType? TourDateId { get; init; }
        public string? Keyword { get; init; }
        public bool PaidOnly { get; init; }
    }

    public static async Task<IReadOnlyList<BookingExportRow>> QueryAsync(IAppDbContext db, ITenantContext tenant, SupplierScope scope,
        Filter filter, CancellationToken cancellationToken)
    {
        var tours = await scope.TourIdsAsync(cancellationToken);
        if (tours is { Count: 0 })
            return [];

        return await db.QueryProcedureAsync<BookingExportRow>("GetBookingsExports", new
        {
            tenant.TenantId,
            TourStartDate = filter.TourStart,
            TourEndDate = filter.TourEnd,
            BookingStartDate = filter.BookingStart,
            BookingEndDate = filter.BookingEnd,
            filter.TourId,
            filter.TourDateId,
            Description = string.IsNullOrWhiteSpace(filter.Keyword) ? null : filter.Keyword,
            filter.HideRefunded,
            TourIds = StoredProcedures.GuidList(tours ?? []),
            filter.PaidOnly,
        }, cancellationToken);
    }

    /// <summary>The legacy manifest columns.</summary>
    public static readonly IReadOnlyList<SpreadsheetColumn<BookingExportRow>> Columns =
    [
        new("Reference", r => r.BookingInvoiceId),
        new("Booking Date", r => r.BookingBookingDate, "dd/mm/yyyy hh:mm"),
        new("Tour", r => r.TourName),
        new("Pickup Location", r => r.PickupLocation),
        new("Tour Start Date", r => r.StartDate, "dd/mm/yyyy hh:mm"),
        new("Tour End Date", r => r.EndDate, "dd/mm/yyyy hh:mm"),
        new("Amount", r => r.Amount, "$#,##0.00"),
        new("Price Label", r => r.PriceLabel),
        // No payment method (unpaid, or an older payment) reads as Stripe, as legacy's export (the enum's default) did.
        new("Payment Method", r => ((BookingPaymentMethod)(r.PaymentMethod ?? 0)).ToString()),
        new("Status", r => BookingStates.Label(r.Status)),
        new("Guest Name", r => r.GuestName),
        new("Date of Birth", r => r.GuestDateOfBirth, "dd/mm/yyyy"),
        new("Nationality", r => r.GuestNationality),
        new("Gender", r => r.GuestGender),
        new("Passport Number", r => r.GuestPassportNumber),
        new("Additional Notes", r => r.BookingAdditionalNotes),
        new("Waiver Signed", r => r.WaiverSigned == true),
        new("Booking Source", r => r.BookingBookingSource),
        new("Booked By", r => r.BookingStaffName),
    ];
}
