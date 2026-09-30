using Microsoft.Extensions.Caching.Memory;
using Fuse.Application.Common.Cloudbeds;
using Fuse.Application.Common.Time;
using Fuse.Application.Features.Identity;
using Fuse.Contracts.Dashboard;

namespace Fuse.Application.Features.Dashboard;

/// <summary>
/// Each hostel's guests in house now (Cloudbeds "checked_in") or arriving today or tomorrow ("confirmed"), less anyone
/// with a tour booked here during their stay (as the lead booker or a guest on a tour). Guests the Cloudbeds import
/// hasn't brought in yet can't have a booking, so they're listed too. Kept for two minutes: paging, sorting and
/// searching the dashboard table don't ask every hostel again.
/// </summary>
internal sealed class GuestsWithoutTours(IAppDbContext db, HostelStays stays, ICloudbedsClient cloudbeds, IBusinessClock clock, IMemoryCache cache)
{
    public sealed record Row(GuestWithoutToursRow Guest, bool InHouse);
    public sealed record Snapshot(IReadOnlyList<Row> Rows, IReadOnlyList<string> Hostels, IReadOnlyList<string> Unreachable);

    private static readonly TimeSpan KeptFor = TimeSpan.FromMinutes(2);
    public const string CacheKey = "dashboard:guests-without-tours";

    public async Task<Snapshot> GetAsync(CancellationToken cancellationToken) =>
        (await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = KeptFor;
            return await BuildAsync(cancellationToken);
        }))!;

    private async Task<Snapshot> BuildAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.Today);
        var asked = await stays.AskEachAsync(async hostel =>
        {
            var inHouse = cloudbeds.ListReservationsAsync(hostel.CloudbedsPropertyId, hostel.ApiKey, "checked_in", null, null, cancellationToken);
            var arriving = cloudbeds.ListReservationsAsync(hostel.CloudbedsPropertyId, hostel.ApiKey, "confirmed", today, today.AddDays(1), cancellationToken);
            return [.. await inHouse, .. await arriving];
        }, cancellationToken);

        // One row per guest per hostel (a guest can hold more than one reservation).
        var found = asked.SelectMany(h => h.Reservations
                .Where(r => !string.IsNullOrWhiteSpace(r.GuestId) && r.EndDate is not null)
                .GroupBy(r => r.GuestId!)
                .Select(g => (h.Hostel, Reservation: g.OrderByDescending(r => r.Status == "checked_in").ThenBy(r => r.StartDate).First())))
            .ToList();

        var cloudbedsIds = found.Select(r => r.Reservation.GuestId!).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => u.CloudbedsGuestId != null && cloudbedsIds.Contains(u.CloudbedsGuestId))
            .Select(u => new { u.Id, u.CloudbedsGuestId })
            .ToListAsync(cancellationToken);
        var userByCloudbeds = users.GroupBy(u => u.CloudbedsGuestId!).ToDictionary(g => g.Key, g => g.First().Id);
        var toursByUser = await HostelStays.TourStartsAsync(db, [.. userByCloudbeds.Values], today,
            found.Count == 0 ? today : found.Max(r => r.Reservation.EndDate!.Value), cancellationToken);

        var rows = found
            .Select(r =>
            {
                var userId = userByCloudbeds.GetValueOrDefault(r.Reservation.GuestId!);
                var checkOut = r.Reservation.EndDate!.Value;
                var booked = userId is not null && toursByUser.TryGetValue(userId, out var starts)
                    && starts.Any(start => DateOnly.FromDateTime(start) >= today && DateOnly.FromDateTime(start) <= checkOut);
                return booked ? null : new Row(new GuestWithoutToursRow
                {
                    Name = string.IsNullOrWhiteSpace(r.Reservation.GuestName) ? "Guest" : r.Reservation.GuestName.Trim(),
                    GuestId = userId,
                    Hostel = r.Hostel.Name,
                    CheckIn = r.Reservation.StartDate ?? today,
                    CheckOut = checkOut,
                    Rooms = HostelStays.RoomsText(r.Reservation.Rooms),
                    ReservationUrl = HostelStays.ReservationUrl(r.Hostel.CloudbedsPropertyId, r.Reservation.ReservationId),
                }, r.Reservation.Status == "checked_in");
            })
            .OfType<Row>()
            .ToList();
        return new(rows, [.. asked.Select(h => h.Hostel.Name)], [.. asked.Where(h => h.Failed).Select(h => h.Hostel.Name)]);
    }
}

internal sealed class SearchGuestsWithoutToursHandler(GuestsWithoutTours guests) : IQueryHandler<SearchGuestsWithoutTours, PagedResult<GuestWithoutToursRow>>
{
    public async ValueTask<PagedResult<GuestWithoutToursRow>> Handle(SearchGuestsWithoutTours query, CancellationToken cancellationToken)
    {
        var rows = (await guests.GetAsync(cancellationToken)).Rows
            .Where(r => r.InHouse != query.Arriving)
            .Select(r => r.Guest)
            .Where(g => string.IsNullOrWhiteSpace(query.Hostel) || g.Hostel.Equals(query.Hostel, StringComparison.OrdinalIgnoreCase))
            .Where(g => string.IsNullOrWhiteSpace(query.Keyword)
                || g.Name.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase)
                || (g.Rooms?.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase) ?? false));
        // Leaving soonest first (in house), or arriving soonest first.
        return rows.AsQueryable()
            .OrderBy(query.OrderBy, query.Arriving ? g => g.CheckIn : g => g.CheckOut)
            .ToPage(query);
    }
}

internal sealed class GetGuestsWithoutToursHostelsHandler(GuestsWithoutTours guests) : IQueryHandler<GetGuestsWithoutToursHostels, GuestsWithoutToursHostels>
{
    public async ValueTask<GuestsWithoutToursHostels> Handle(GetGuestsWithoutToursHostels query, CancellationToken cancellationToken)
    {
        var snapshot = await guests.GetAsync(cancellationToken);
        return new(snapshot.Hostels, snapshot.Unreachable);
    }
}
