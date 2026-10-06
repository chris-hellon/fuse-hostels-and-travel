namespace Fuse.Application.Common.Cloudbeds;

/// <summary>The Cloudbeds PMS API (read-only calls). Each property has its own Cloudbeds account and API key.</summary>
public interface ICloudbedsClient
{
    /// <summary>The guest from one property's account; null when that account doesn't know the guest id.</summary>
    Task<CloudbedsGuest?> GetGuestAsync(string propertyId, string apiKey, string guestId, CancellationToken cancellationToken);

    /// <summary>All of a property's guests with a reservation in <paramref name="status"/> (e.g. "in_house",
    /// "not_checked_in"), every page (legacy getGuestsByStatus, for the guest import).</summary>
    Task<IReadOnlyList<CloudbedsGuest>> ListGuestsByStatusAsync(string propertyId, string apiKey, string status, CancellationToken cancellationToken);

    /// <summary>A property's reservations that the guest is on (as the main or an additional guest).</summary>
    Task<IReadOnlyList<CloudbedsReservation>> ListGuestReservationsAsync(string propertyId, string apiKey, string guestId, CancellationToken cancellationToken);

    /// <summary>A property's reservations in <paramref name="status"/> (checked_in = in house; confirmed = booked; null = any),
    /// optionally only those checking in between the two dates. Without <paramref name="includeRooms"/> Cloudbeds isn't asked
    /// for every room (smaller, quicker pages) and <see cref="CloudbedsReservation.Rooms"/> may be empty.</summary>
    Task<IReadOnlyList<CloudbedsReservation>> ListReservationsAsync(string propertyId, string apiKey, string? status, DateOnly? checkInFrom,
        DateOnly? checkInTo, CancellationToken cancellationToken, bool includeRooms = true);
}

/// <summary>The fields of Cloudbeds' <c>getReservations</c> we show. <see cref="Status"/> as Cloudbeds sends it
/// (not_confirmed, confirmed, canceled, checked_in, checked_out, no_show).</summary>
public sealed record CloudbedsReservation(string ReservationId, string? Status, DateOnly? StartDate, DateOnly? EndDate, decimal Balance,
    string? Source, IReadOnlyList<CloudbedsRoom> Rooms, string? GuestId = null, string? GuestName = null);

/// <summary>A room (or bed) on a reservation: its type ("6 Bed En-suite Dorm") and, once assigned, its name ("R307 - Bed 109").</summary>
public sealed record CloudbedsRoom(string TypeName, string? RoomName);

/// <summary>The fields of Cloudbeds' <c>getGuest</c> response that we use. Dates arrive as strings (sometimes blank).</summary>
public sealed record CloudbedsGuest
{
    /// <summary>Set by the guest lists (getGuest is asked by id).</summary>
    public string? GuestId { get; init; }
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Gender { get; init; }
    public string? Phone { get; init; }
    public string? Country { get; init; }
    public string? BirthDate { get; init; }
    public string? DocumentType { get; init; }
    public string? DocumentNumber { get; init; }
    public string? DocumentIssueDate { get; init; }
    public string? DocumentIssuingCountry { get; init; }
    public string? DocumentExpirationDate { get; init; }
    public IReadOnlyList<CloudbedsCustomField> CustomFields { get; init; } = [];
}

public sealed record CloudbedsCustomField(string? Name, string? Value);
