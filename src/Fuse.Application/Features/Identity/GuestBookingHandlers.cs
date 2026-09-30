using Fuse.Application.Common.Time;
using Fuse.Application.Common.Cloudbeds;
using Fuse.Application.Features.Tours;
using Fuse.Contracts.Dashboard;
using Fuse.Contracts.Identity;

namespace Fuse.Application.Features.Identity;

internal sealed class ListGuestTourBookingsHandler(IAppDbContext db, SupplierScope scope) : IQueryHandler<ListGuestTourBookings, IReadOnlyList<GuestTourBooking>>
{
    /// <summary>A bound: the most recent ones.</summary>
    private const int Max = 100;

    public async ValueTask<IReadOnlyList<GuestTourBooking>> Handle(ListGuestTourBookings query, CancellationToken cancellationToken)
    {
        var id = query.Id;
        var guid = DefaultIdType.TryParse(id, out var parsed) ? parsed : DefaultIdType.Empty;
        var bookings = db.Bookings.AsNoTracking()
            .Where(b => b.GuestId == id || b.Items.Any(i => i.Guests!.Any(g => g.GuestId == guid)));
        // Suppliers see only bookings of their tours, as on Bookings.
        if (await scope.TourIdsAsync(cancellationToken) is { } tours)
            bookings = bookings.Where(b => b.Items.Any(i => i.TourId != null && tours.Contains(i.TourId.Value)));

        var rows = await bookings
            .OrderByDescending(b => b.BookingDate)
            .Take(Max)
            .Select(b => new
            {
                b.Id, b.InvoiceId, b.BookingDate, b.TotalAmount, b.GuestId, b.IsPaid, b.AmountOutstanding,
                Cancelled = b.Cancelled == true, Refunded = b.Refunded == true, PartialRefund = b.PartialRefund == true,
                Tours = b.Items.Where(i => i.TourId != null).OrderBy(i => i.StartDate)
                    .Select(i => new GuestTourBookingItem(i.Tour != null ? i.Tour.Name : "Tour", i.StartDate, i.Cancelled == true)).ToList(),
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(b => new GuestTourBooking
        {
            BookingId = b.Id,
            Reference = b.InvoiceId,
            BookingDate = b.BookingDate,
            TotalAmount = b.TotalAmount,
            LeadBooker = b.GuestId == id,
            Tours = b.Tours,
            // As the Bookings table's Status.
            Status = b.Cancelled ? BookingState.Cancelled
                : b.Refunded ? BookingState.Refunded
                : b.PartialRefund ? BookingState.PartialRefund
                : b.AmountOutstanding > 0 ? BookingState.PartiallyPaid
                : b.IsPaid ? BookingState.Paid
                : BookingState.Unpaid,
        })];
    }
}

/// <summary>
/// Hostel stays from Cloudbeds, shared by the guest's bookings, their current stay and the dashboard: each hostel's
/// account is asked at once, and a hostel that can't be asked is named rather than failing the lot.
/// </summary>
internal sealed class HostelStays(IAppDbContext db, ICloudbedsClient cloudbeds, ILogger<HostelStays> logger)
{
    public sealed record Hostel(string Name, string CloudbedsPropertyId, string ApiKey);

    public async Task<IReadOnlyList<Hostel>> HostelsAsync(CancellationToken cancellationToken) =>
        (await db.Properties.AsNoTracking()
            .Where(p => p.CloudbedsPropertyId != null && p.CloudbedsPropertyId != "" && p.CloudbedsApiKey != null && p.CloudbedsApiKey != "")
            .OrderBy(p => p.Name)
            .Select(p => new { p.Name, p.CloudbedsPropertyId, p.CloudbedsApiKey })
            .ToListAsync(cancellationToken))
        .Select(p => new Hostel(p.Name, p.CloudbedsPropertyId!, p.CloudbedsApiKey!)).ToList();

    /// <summary>Asks each hostel with <paramref name="ask"/>; each result says whether that hostel could be asked.</summary>
    public async Task<IReadOnlyList<(Hostel Hostel, IReadOnlyList<CloudbedsReservation> Reservations, bool Failed)>> AskEachAsync(
        Func<Hostel, Task<IReadOnlyList<CloudbedsReservation>>> ask, CancellationToken cancellationToken) =>
        await AskEachAsync(await HostelsAsync(cancellationToken), ask);

    /// <summary>As above, for hostels already read (Cloudbeds only: nothing here uses the database).</summary>
    public async Task<IReadOnlyList<(Hostel Hostel, IReadOnlyList<CloudbedsReservation> Reservations, bool Failed)>> AskEachAsync(
        IReadOnlyList<Hostel> hostels, Func<Hostel, Task<IReadOnlyList<CloudbedsReservation>>> ask) =>
        await Task.WhenAll(hostels.Select(async hostel =>
        {
            try
            {
                return (hostel, await ask(hostel), false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Cloudbeds reservations couldn't be read from {Hostel}", hostel.Name);
                return (hostel, (IReadOnlyList<CloudbedsReservation>)[], true);
            }
        }));

    /// <summary>Every stay the guest's Cloudbeds id is on, newest check-in first.</summary>
    public async Task<GuestHostelStays> ForGuestAsync(string? cloudbedsGuestId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cloudbedsGuestId))
            return new(false, [], []);
        var asked = await AskEachAsync(hostel => cloudbeds.ListGuestReservationsAsync(hostel.CloudbedsPropertyId, hostel.ApiKey, cloudbedsGuestId, cancellationToken),
            cancellationToken);
        var stays = asked.SelectMany(h => h.Reservations.Select(r => Stay(h.Hostel, r))).OrderByDescending(s => s.CheckIn).ToList();
        return new(true, stays, [.. asked.Where(h => h.Failed).Select(h => h.Hostel.Name)]);
    }

    public static GuestHostelStay Stay(Hostel hostel, CloudbedsReservation r) => new()
    {
        Hostel = hostel.Name,
        ReservationId = r.ReservationId,
        CheckIn = r.StartDate ?? default,
        CheckOut = r.EndDate ?? default,
        Nights = r.StartDate is { } from && r.EndDate is { } to ? Math.Max(0, to.DayNumber - from.DayNumber) : 0,
        Status = StatusLabel(r.Status),
        Rooms = RoomsText(r.Rooms),
        Balance = r.Balance,
        Source = r.Source,
        Url = ReservationUrl(hostel.CloudbedsPropertyId, r.ReservationId),
    };

    /// <summary>Cloudbeds' documented link to a reservation by its number ("r" + reservation id).</summary>
    internal static string ReservationUrl(string propertyId, string reservationId) =>
        $"https://hotels.cloudbeds.com/connect/{Uri.EscapeDataString(propertyId)}#/reservations/r{Uri.EscapeDataString(reservationId)}";

    /// <summary>Each room type once: "6 Bed En-suite Dorm ×2 · R307 - Bed 109, R307 - Bed 111".</summary>
    internal static string? RoomsText(IReadOnlyList<CloudbedsRoom> rooms) =>
        rooms.Count == 0 ? null : string.Join("; ", rooms.GroupBy(r => r.TypeName).Select(type =>
        {
            var names = type.Select(r => r.RoomName).OfType<string>().Distinct().ToList();
            var count = type.Count() > 1 ? $" ×{type.Count()}" : "";
            return names.Count == 0 ? $"{type.Key}{count}" : $"{type.Key}{count} · {string.Join(", ", names)}";
        }));

    public const string InHouse = "In house";

    /// <summary>
    /// Each guest's tour start times from <paramref name="from"/> through <paramref name="until"/>, as the lead booker or a
    /// guest on the tour (cancelled tours and bookings left out).
    /// </summary>
    public static async Task<Dictionary<string, List<DateTime>>> TourStartsAsync(IAppDbContext db, IReadOnlyList<string> userIds, DateOnly from,
        DateOnly until, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
            return [];
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = until.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var live = db.BookingItems.AsNoTracking()
            .Where(i => i.TourId != null && i.StartDate >= start && i.StartDate < end && i.Cancelled != true && i.Booking.Cancelled != true);
        var asLead = await live.Where(i => i.Booking.GuestId != null && userIds.Contains(i.Booking.GuestId))
            .Select(i => new { UserId = i.Booking.GuestId!, i.StartDate })
            .ToListAsync(cancellationToken);
        var guids = userIds.Select(id => DefaultIdType.TryParse(id, out var guid) ? guid : DefaultIdType.Empty).Where(g => g != DefaultIdType.Empty).ToList();
        var asGuest = await live.SelectMany(i => i.Guests!.Where(g => g.Cancelled != true && guids.Contains(g.GuestId)), (i, g) => new { g.GuestId, i.StartDate })
            .ToListAsync(cancellationToken);
        return asLead.Select(x => (x.UserId, x.StartDate))
            .Concat(asGuest.Select(x => (UserId: x.GuestId.ToString(), x.StartDate)))
            .GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.StartDate).ToList(), StringComparer.OrdinalIgnoreCase);
    }

    internal static string StatusLabel(string? status) => status switch
    {
        "not_confirmed" => "Not confirmed",
        "confirmed" => "Confirmed",
        "checked_in" => InHouse,
        "checked_out" => "Checked out",
        "canceled" or "cancelled" => "Cancelled",
        "no_show" => "No show",
        null or "" => "—",
        _ => status.Replace('_', ' '),
    };
}

internal sealed class ListGuestHostelStaysHandler(UserKinds kinds, HostelStays stays) : IQueryHandler<ListGuestHostelStays, GuestHostelStays>
{
    public async ValueTask<GuestHostelStays> Handle(ListGuestHostelStays query, CancellationToken cancellationToken) =>
        await stays.ForGuestAsync((await kinds.GetDetailsAsync(query.Id, staff: false, cancellationToken)).CloudbedsGuestId, cancellationToken);
}

/// <summary>The stay they're in now, or else their next confirmed one (booking wizard, booking details).</summary>
internal sealed class GetGuestCurrentStayHandler(UserKinds kinds, HostelStays stays, IBusinessClock clock) : IQueryHandler<GetGuestCurrentStay, GuestCurrentStay>
{
    public async ValueTask<GuestCurrentStay> Handle(GetGuestCurrentStay query, CancellationToken cancellationToken)
    {
        var all = (await stays.ForGuestAsync((await kinds.GetDetailsAsync(query.Id, staff: false, cancellationToken)).CloudbedsGuestId, cancellationToken)).Stays;
        var today = DateOnly.FromDateTime(clock.Today);
        if (all.FirstOrDefault(s => s.Status == HostelStays.InHouse) is { } inHouse)
            return new(inHouse, true);
        var next = all.Where(s => s.Status is "Confirmed" or "Not confirmed" && s.CheckOut >= today).OrderBy(s => s.CheckIn).FirstOrDefault();
        if (next is not null)
            return new(next, false);
        // Nothing now or next: their most recent reservation (one they stayed on before a cancelled one).
        var last = all.Where(s => s.Status is not ("Cancelled" or "No show")).OrderByDescending(s => s.CheckIn).FirstOrDefault()
            ?? all.OrderByDescending(s => s.CheckIn).FirstOrDefault();
        return new(null, false, last);
    }
}
