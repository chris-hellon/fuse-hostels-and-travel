namespace Fuse.Domain.Catalog.Bookings;

public class BookingItemGuest : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private BookingItemGuest() { }

    public DefaultIdType BookingItemId { get; private set; } = Guid.Empty!;
    public DefaultIdType GuestId { get; private set; } = Guid.Empty!;
    public bool? Cancelled { get; private set; }
    public bool? NoShow { get; private set; }
    public bool? CheckedIn { get; private set; }

    public virtual BookingItem BookingItem { get; private set; } = null!;

    public BookingItemGuest(DefaultIdType guestId)
    {
        GuestId = guestId;
    }

    /// <summary>The manifest's attendance: exactly one of checked in / no show / cancelled (or none).</summary>
    public void SetAttendance(bool checkedIn, bool noShow, bool cancelled) => (CheckedIn, NoShow, Cancelled) = (checkedIn, noShow, cancelled);

    public void Cancel() => Cancelled = true;
}