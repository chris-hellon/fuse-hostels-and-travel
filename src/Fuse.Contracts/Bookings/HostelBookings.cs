namespace Fuse.Contracts.Bookings;

// Bookings › Hostel Bookings: each hostel's reservations from its Cloudbeds account, read-only (nothing is booked,
// changed or stored here). Each reservation's guest is matched to our customer by their Cloudbeds guest id (as the
// Cloudbeds guest import links them), so their tour bookings show beside the stay. Kept for a couple of minutes so
// paging, sorting and searching don't ask every hostel again.

public enum HostelBookingsView
{
    /// <summary>Checked in now.</summary>
    InHouse,
    /// <summary>Confirmed, checking in within the next week.</summary>
    Arriving,
    /// <summary>Any status, checking in between the chosen dates.</summary>
    All,
}

public sealed record HostelBookingRow
{
    public string ReservationId { get; init; } = "";
    public string Hostel { get; init; } = "";
    public string GuestName { get; init; } = "";
    /// <summary>Our customer; null when the Cloudbeds import hasn't brought them in yet.</summary>
    [NotSortable] public string? GuestId { get; init; }
    [NotSortable] public string? GuestEmail { get; init; }
    [NotSortable] public string? GuestPhone { get; init; }
    public DateOnly CheckIn { get; init; }
    public DateOnly CheckOut { get; init; }
    public int Nights { get; init; }
    /// <summary>Cloudbeds' status, in words ("Confirmed", "In house", "Checked out", "Cancelled", ...).</summary>
    public string Status { get; init; } = "";
    /// <summary>Each room type once, with how many and which beds.</summary>
    public string? Rooms { get; init; }
    public decimal Balance { get; init; }
    public string? Source { get; init; }
    /// <summary>The customer's tour bookings with a tour during the stay (as the lead booker or a guest on it).</summary>
    public int Tours { get; init; }
    /// <summary>The reservation in Cloudbeds (for staff signed in to that hostel's account).</summary>
    [NotSortable] public string? Url { get; init; }
}

/// <summary>By default: in house leaving soonest first; arriving soonest first; all by check-in.</summary>
[Post("bookings/hostel-bookings/search"), Permission(FuseAction.View, FuseResource.TourBookings)]
public sealed record SearchHostelBookings : PagedQuery, IQuery<PagedResult<HostelBookingRow>>
{
    public HostelBookingsView View { get; init; }
    /// <summary>Only this hostel's reservations.</summary>
    public string? Hostel { get; init; }
    /// <summary>For <see cref="HostelBookingsView.All"/>: checking in from / to (a week either side of today when not set).</summary>
    public DateOnly? CheckInFrom { get; init; }
    public DateOnly? CheckInTo { get; init; }
}

/// <param name="Hostels">Hostels connected to Cloudbeds (the filter).</param>
/// <param name="Unreachable">Hostels whose Cloudbeds couldn't be asked for this view just now (their reservations are missing).</param>
public sealed record HostelBookingHostels(IReadOnlyList<string> Hostels, IReadOnlyList<string> Unreachable);

[Post("bookings/hostel-bookings/hostels"), Permission(FuseAction.View, FuseResource.TourBookings)]
public sealed record GetHostelBookingHostels : IQuery<HostelBookingHostels>
{
    public HostelBookingsView View { get; init; }
    public DateOnly? CheckInFrom { get; init; }
    public DateOnly? CheckInTo { get; init; }
}
