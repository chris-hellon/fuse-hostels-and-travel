using Fuse.Contracts.Dashboard;

namespace Fuse.Contracts.Identity;

// A guest's bookings on their profile and in the Edit Guest dialog: tour bookings here (as the lead booker or a guest
// on a tour), and hostel stays from each hostel's Cloudbeds account (read-only, asked live; nothing is stored).

public sealed record GuestTourBooking
{
    public DefaultIdType BookingId { get; init; }
    public int Reference { get; init; }
    public DateTime BookingDate { get; init; }
    public BookingState Status { get; init; }
    public decimal TotalAmount { get; init; }
    /// <summary>False when they're a guest on someone else's booking.</summary>
    public bool LeadBooker { get; init; }
    public IReadOnlyList<GuestTourBookingItem> Tours { get; init; } = [];
}

public sealed record GuestTourBookingItem(string TourName, DateTime StartDate, bool Cancelled);

/// <summary>Newest first.</summary>
[Get("guests/{Id}/bookings"), Permission(FuseAction.View, FuseResource.TourBookings)]
public sealed record ListGuestTourBookings(string Id) : IQuery<IReadOnlyList<GuestTourBooking>>;

public sealed record GuestHostelStay
{
    public string Hostel { get; init; } = "";
    public string ReservationId { get; init; } = "";
    public DateOnly CheckIn { get; init; }
    public DateOnly CheckOut { get; init; }
    public int Nights { get; init; }
    /// <summary>Cloudbeds' status, in words ("Confirmed", "In house", "Checked out", "Cancelled", ...).</summary>
    public string Status { get; init; } = "";
    /// <summary>Each room type once, with how many and which beds: "6 Bed En-suite Dorm ×2 · R307 - Bed 109, R307 - Bed 111".</summary>
    public string? Rooms { get; init; }
    public decimal Balance { get; init; }
    public string? Source { get; init; }
    /// <summary>The reservation in Cloudbeds (for staff signed in to that hostel's account).</summary>
    public string? Url { get; init; }
}

/// <param name="Linked">False when the guest has no Cloudbeds guest id (nothing to ask).</param>
/// <param name="Unreachable">Hostels whose Cloudbeds couldn't be asked just now (their stays may be missing).</param>
public sealed record GuestHostelStays(bool Linked, IReadOnlyList<GuestHostelStay> Stays, IReadOnlyList<string> Unreachable);

/// <summary>Newest check-in first.</summary>
[Get("guests/{Id}/hostel-stays"), Permission(FuseAction.View, FuseResource.Guests)]
public sealed record ListGuestHostelStays(string Id) : IQuery<GuestHostelStays>;

/// <param name="Stay">The stay they're in now, or else their next one; null when neither (or not linked to Cloudbeds).</param>
/// <param name="Last">When there's no <paramref name="Stay"/>: their most recent reservation (checked out, or cancelled when that's
/// all there is), so staff still see where they stayed.</param>
public sealed record GuestCurrentStay(GuestHostelStay? Stay, bool InHouse, GuestHostelStay? Last = null);

/// <summary>The guest's hostel stay now or next (booking wizard, booking details): to book tours during it.</summary>
[Get("guests/{Id}/current-stay"), Permission(FuseAction.View, FuseResource.TourBookings)]
public sealed record GetGuestCurrentStay(string Id) : IQuery<GuestCurrentStay>;
