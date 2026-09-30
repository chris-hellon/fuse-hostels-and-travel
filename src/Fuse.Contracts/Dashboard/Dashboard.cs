using Fuse.Contracts.Tours;

namespace Fuse.Contracts.Dashboard;

/// <summary>
/// The dashboard's cards. The "Today" figures always cover today (business time zone); the "Total"
/// figures cover the chosen range of booking dates. Counts are per booked tour (booking item).
/// </summary>
public sealed record DashboardStats
{
    public int TodaysBookings { get; init; }
    /// <summary>Guests (lead + additional) departing today.</summary>
    public int TodaysDepartures { get; init; }
    public decimal TodaysRevenue { get; init; }
    public int TotalBookings { get; init; }
    public decimal TotalRevenue { get; init; }
    public int TotalCancelled { get; init; }
    public decimal TotalCancelledValue { get; init; }
    public int TotalRefunded { get; init; }
    public decimal TotalRefundedValue { get; init; }
}

/// <summary>Booking dates (inclusive) for the "Total" cards; null = the last 30 days.</summary>
[Post("dashboard/stats"), Permission(FuseAction.View, FuseResource.Dashboard)]
public sealed record GetDashboardStats(DateTime? From, DateTime? To) : IQuery<DashboardStats>;

/// <summary>A guest on a tour departing on a day (the Departures tables and their exports).</summary>
public sealed record DepartureRow
{
    public int Reference { get; init; }
    public string TourName { get; init; } = "";
    public string? GuestId { get; init; }
    public string? GuestName { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public string? PickupLocation { get; init; }
    public bool WaiverSigned { get; init; }
    public BookingState Status { get; init; }
}

/// <summary>Where a booking (or a guest on it) stands, most significant first.</summary>
public enum BookingState
{
    Unpaid,
    PartiallyPaid,
    Paid,
    CheckedIn,
    Refunded,
    NoShow,
    Cancelled,
    PartialRefund,
}

public static class BookingStates
{
    public static string Label(BookingState state) => state switch
    {
        BookingState.PartiallyPaid => "Partially Paid",
        BookingState.CheckedIn => "Checked In",
        BookingState.NoShow => "No Show",
        BookingState.PartialRefund => "Partial Refund",
        _ => state.ToString(),
    };
}

/// <summary>
/// Everyone departing on a day (any time that day), refunds hidden. Suppliers see their own tours.
/// The day is counted from today in the business's time zone: 0 = today, 1 = tomorrow.
/// </summary>
[Post("dashboard/departures"), Permission(FuseAction.View, FuseResource.Dashboard)]
public sealed record SearchDepartures : PagedQuery, IQuery<PagedResult<DepartureRow>>
{
    public int DaysFromToday { get; init; }
    public DefaultIdType? TourId { get; init; }
}

/// <summary>The same guests as <see cref="SearchDepartures"/>, as a spreadsheet (legacy columns).</summary>
[Post("dashboard/departures/export"), Permission(FuseAction.Export, FuseResource.Bookings)]
public sealed record ExportDepartures(int DaysFromToday, DefaultIdType? TourId, string? Keyword) : IQuery<FileDownload>;

public sealed class SearchDeparturesValidator : AbstractValidator<SearchDepartures>
{
    public SearchDeparturesValidator() => RuleFor(x => x.DaysFromToday).InclusiveBetween(-7, 7).WithName("Day");
}

public sealed class ExportDeparturesValidator : AbstractValidator<ExportDepartures>
{
    public ExportDeparturesValidator() => RuleFor(x => x.DaysFromToday).InclusiveBetween(-7, 7).WithName("Day");
}

/// <summary>A tour booking made today (Today's activity).</summary>
public sealed record BookingActivityRow
{
    public DefaultIdType Id { get; init; }
    public int Reference { get; init; }
    public string Description { get; init; } = "";
    public string? GuestId { get; init; }
    public string? GuestName { get; init; }
    public DateTime BookingDate { get; init; }
    public decimal TotalAmount { get; init; }
    public bool IsPaid { get; init; }
    public decimal? AmountOutstanding { get; init; }
    public bool? Refunded { get; init; }
    public bool? Cancelled { get; init; }

    [NotSortable]
    public BookingState Status => Cancelled == true ? BookingState.Cancelled
        : Refunded == true ? BookingState.Refunded
        : IsPaid ? BookingState.Paid
        : AmountOutstanding is > 0 && AmountOutstanding < TotalAmount ? BookingState.PartiallyPaid
        : BookingState.Unpaid;
}

[Post("dashboard/activity"), Permission(FuseAction.View, FuseResource.Dashboard)]
public sealed record SearchTodaysBookings : PagedQuery, IQuery<PagedResult<BookingActivityRow>>
{
    public DefaultIdType? TourId { get; init; }
}

/// <summary>Tours for the dashboard and calendar filters (a supplier's own tours only).</summary>
[Get("dashboard/tours"), Permission(FuseAction.View, FuseResource.Dashboard)]
public sealed record ListDashboardTours : IQuery<IReadOnlyList<LookupItem>>;
