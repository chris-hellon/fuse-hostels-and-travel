namespace Fuse.Contracts.Reports;

// Legacy Reports › Staff Bookings: each staff member's paid tour sales booked in a period (by booking date, Vietnam
// time), with their commission under the tours', packages' and pack incentives' commission rules. A sale is credited
// to whoever made the booking (Booking.CreatedBy), including website bookings from their booking QR code. Figures come
// from the GetStaffBookingsReport proc (shared with legacy); a staff member's total is the sum of their tour lines.

/// <summary>One tour, package or pack incentive a staff member sold.</summary>
public sealed record StaffBookingLine
{
    public string TourName { get; init; } = "";
    public int QuantityBooked { get; init; }
    /// <summary>Of <see cref="QuantityBooked"/>, the guests who met the commission rules and earned commission.</summary>
    public int EligiblePax { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal TotalCommissionAmount { get; init; }
}

public sealed record StaffBookingRow
{
    public DefaultIdType UserId { get; init; }
    public string FullName { get; init; } = "";
    public int BookingsMade { get; init; }
    /// <summary>Guests booked (the sum of the lines' <see cref="StaffBookingLine.QuantityBooked"/>; legacy's "Total Items Booked").</summary>
    public int ItemsCount { get; init; }
    public decimal TotalBookingsAmount { get; init; }
    public decimal TotalCommission { get; init; }
    public IReadOnlyList<StaffBookingLine> Lines { get; init; } = [];
}

/// <summary>Staff with sales booked between <see cref="From"/> and <see cref="To"/> (whole days; by default the last
/// month to today, as legacy); optionally only tours visiting a location (e.g. Hoi An vs the North, which have their own
/// commission rules).</summary>
[Post("reports/staff-bookings/search"), Permission(FuseAction.View, FuseResource.Reports)]
public sealed record SearchStaffBookings : PagedQuery, IQuery<PagedResult<StaffBookingRow>>
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public DefaultIdType? LocationId { get; init; }
}

/// <summary>The report as a spreadsheet (as legacy): a row per staff member with their totals.</summary>
[Post("reports/staff-bookings/export"), Permission(FuseAction.Export, FuseResource.Reports)]
public sealed record ExportStaffBookings(DateOnly? From, DateOnly? To, DefaultIdType? LocationId, string? Keyword) : IQuery<FileDownload>;

public sealed class SearchStaffBookingsValidator : AbstractValidator<SearchStaffBookings>
{
    public SearchStaffBookingsValidator() =>
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From).When(q => q.From is not null && q.To is not null).WithMessage("The end date must be on or after the start date.");
}

public sealed class ExportStaffBookingsValidator : AbstractValidator<ExportStaffBookings>
{
    public ExportStaffBookingsValidator() =>
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From).When(q => q.From is not null && q.To is not null).WithMessage("The end date must be on or after the start date.");
}
