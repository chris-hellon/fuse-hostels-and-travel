namespace Fuse.Contracts.Calendar;

/// <summary>A tour departure with paid, active guests (the booking calendar), with its tour's category (the filter).</summary>
public sealed record CalendarDeparture(DefaultIdType TourId, string TourName, DateTime Start, DateTime End, int Guests, string? Category = null);

/// <summary>Departures starting between the two days (inclusive). Suppliers see their own tours.</summary>
[Post("calendar/departures"), Permission(FuseAction.View, FuseResource.Calendar)]
public sealed record ListCalendarDepartures(DateTime From, DateTime To) : IQuery<IReadOnlyList<CalendarDeparture>>;

/// <summary>A guest on a departure (lead or additional), with their booking (to open it).</summary>
public sealed record DepartureGuest(string? GuestId, int Reference, string? GuestName, bool WaiverSigned, string? PickupLocation, DefaultIdType? BookingId = null);

[Post("calendar/departures/guests"), Permission(FuseAction.View, FuseResource.Calendar)]
public sealed record ListDepartureGuests(DefaultIdType TourId, DateTime Start) : IQuery<IReadOnlyList<DepartureGuest>>;

/// <summary>The departure's guests as a spreadsheet (the legacy manifest columns).</summary>
[Post("calendar/departures/export"), Permission(FuseAction.View, FuseResource.Calendar)]
public sealed record ExportDepartureGuests(DefaultIdType TourId, DateTime Start) : IQuery<FileDownload>;
