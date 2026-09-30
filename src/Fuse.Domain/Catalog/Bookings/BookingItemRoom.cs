namespace Fuse.Domain.Catalog.Bookings;

public class BookingItemRoom : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private BookingItemRoom() { }

    public DefaultIdType BookingItemId { get; private set; } = Guid.Empty!;
    public string RoomName { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public int Nights { get; private set; }
    public DateTime CheckInDate { get; private set; }
    public DateTime CheckOutDate { get; private set; }
    public string GuestFirstName { get; private set; } = null!;
    public string GuestLastName { get; private set; } = null!;
    public string CloudbedsGuestId { get; private set; } = null!;

    public BookingItemRoom(DefaultIdType bookingItemId, string roomName, decimal amount, int nights, DateTime checkInDate, DateTime checkOutDate, string guestFirstName, string guestLastName, string cloudbedsGuestId)
    {
        BookingItemId = bookingItemId;
        RoomName = roomName;
        Amount = amount;
        Nights = nights;
        CheckInDate = checkInDate;
        CheckOutDate = checkOutDate;
        GuestFirstName = guestFirstName;
        GuestLastName = guestLastName;
        CloudbedsGuestId = cloudbedsGuestId;
    }

    public BookingItemRoom Update(
        string? roomName = null,
        decimal? amount = null,
        int? nights = null,
        DateTime? checkInDate = null,
        DateTime? checkOutDate = null,
        string? guestFirstName = null,
        string? guestLastName = null,
        string? cloudbedsGuestId = null)
    {
        if (roomName is not null && RoomName != roomName)
            RoomName = roomName;

        if (amount is not null && Amount != amount)
            Amount = amount.Value;

        if (nights is not null && Nights != nights)
            Nights = nights.Value;

        if (checkInDate is not null && CheckInDate != checkInDate)
            CheckInDate = checkInDate.Value;

        if (checkOutDate is not null && CheckOutDate != checkOutDate)
            CheckOutDate = checkOutDate.Value;

        if (guestFirstName is not null && GuestFirstName != guestFirstName)
            GuestFirstName = guestFirstName;

        if (guestLastName is not null && GuestLastName != guestLastName)
            GuestLastName = guestLastName;

        if (cloudbedsGuestId is not null && CloudbedsGuestId != cloudbedsGuestId)
            CloudbedsGuestId = cloudbedsGuestId;

        return this;
    }
}