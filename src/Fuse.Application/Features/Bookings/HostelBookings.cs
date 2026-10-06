using Microsoft.Extensions.Caching.Memory;
using Fuse.Application.Common.Cloudbeds;
using Fuse.Application.Common.Files;
using Fuse.Application.Common.Time;
using Fuse.Application.Features.Identity;
using Fuse.Contracts.Bookings;

namespace Fuse.Application.Features.Bookings;

/// <summary>
/// Bookings › Hostel Bookings: each hostel's Cloudbeds reservations for a view, every guest matched to our customer by
/// their Cloudbeds guest id, with how many tour bookings they have during the stay. Read-only. Cloudbeds' answer is kept
/// for two minutes per view: paging, sorting and searching don't ask every hostel again.
/// </summary>
internal sealed class HostelBookings(IAppDbContext db, HostelStays stays, ICloudbedsClient cloudbeds, IBusinessClock clock, IMemoryCache cache)
{
    public sealed record Snapshot(IReadOnlyList<HostelBookingRow> Rows, IReadOnlyList<string> Hostels, IReadOnlyList<string> Unreachable);

    private static readonly TimeSpan KeptFor = TimeSpan.FromMinutes(2);

    /// <summary>How far ahead Arriving looks, and either side of today All looks when no dates are chosen.</summary>
    public const int ArrivingDays = 7, AllDays = 7;

    public static string CacheKey(HostelBookingsView view, DateOnly? from, DateOnly? to) => $"hostel-bookings:{view}:{from:yyyyMMdd}:{to:yyyyMMdd}";

    public async Task<Snapshot> GetAsync(HostelBookingsView view, DateOnly? checkInFrom, DateOnly? checkInTo, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.Today);
        var (status, from, to) = view switch
        {
            HostelBookingsView.InHouse => ("checked_in", (DateOnly?)null, (DateOnly?)null),
            HostelBookingsView.Arriving => ("confirmed", today, today.AddDays(ArrivingDays)),
            _ => ((string?)null, checkInFrom ?? today.AddDays(-AllDays), checkInTo ?? today.AddDays(AllDays)),
        };
        // Only Cloudbeds' answer is kept (and shared: the table and the hostel list asking at once make one round of
        // calls). It runs on its own, outliving whichever request started it, so it never touches that request's
        // database; each request matches customers and counts tours with its own.
        var hostels = await stays.HostelsAsync(cancellationToken);
        var key = CacheKey(view, from, to);
        var fetch = cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = KeptFor;
            return stays.AskEachAsync(hostels, hostel =>
                cloudbeds.ListReservationsAsync(hostel.CloudbedsPropertyId, hostel.ApiKey, status, from, to, CancellationToken.None));
        })!;
        IReadOnlyList<(HostelStays.Hostel Hostel, IReadOnlyList<CloudbedsReservation> Reservations, bool Failed)> asked;
        try
        {
            asked = await fetch.WaitAsync(cancellationToken);
        }
        catch (Exception) when (fetch.IsFaulted)
        {
            cache.Remove(key);
            throw;
        }
        return await BuildAsync(asked, cancellationToken);
    }

    private async Task<Snapshot> BuildAsync(IReadOnlyList<(HostelStays.Hostel Hostel, IReadOnlyList<CloudbedsReservation> Reservations, bool Failed)> asked,
        CancellationToken cancellationToken)
    {
        var found = asked.SelectMany(h => h.Reservations.Select(r => (h.Hostel, Reservation: r))).ToList();

        var cloudbedsIds = found.Select(r => r.Reservation.GuestId).OfType<string>().Where(id => id != "").Distinct().ToList();
        var customers = (await db.Users.AsNoTracking()
                .Where(u => u.CloudbedsGuestId != null && cloudbedsIds.Contains(u.CloudbedsGuestId))
                .Select(u => new { u.Id, u.CloudbedsGuestId, u.Email, u.PhoneNumber })
                .ToListAsync(cancellationToken))
            .GroupBy(u => u.CloudbedsGuestId!)
            .ToDictionary(g => g.Key, g => g.First());
        var dated = found.Where(r => r.Reservation.StartDate is not null && r.Reservation.EndDate is not null).ToList();
        var tours = dated.Count == 0 ? []
            : await HostelStays.TourStartsAsync(db, [.. customers.Values.Select(c => c.Id)], dated.Min(r => r.Reservation.StartDate!.Value),
                dated.Max(r => r.Reservation.EndDate!.Value), cancellationToken);

        var rows = found.Select(r =>
        {
            var stay = HostelStays.Stay(r.Hostel, r.Reservation);
            var customer = r.Reservation.GuestId is { } guestId ? customers.GetValueOrDefault(guestId) : null;
            var during = customer is not null && tours.TryGetValue(customer.Id, out var starts)
                ? starts.Count(start => DateOnly.FromDateTime(start) >= stay.CheckIn && DateOnly.FromDateTime(start) <= stay.CheckOut)
                : 0;
            return new HostelBookingRow
            {
                ReservationId = stay.ReservationId,
                Hostel = stay.Hostel,
                GuestName = string.IsNullOrWhiteSpace(r.Reservation.GuestName) ? "Guest" : r.Reservation.GuestName.Trim(),
                GuestId = customer?.Id,
                GuestEmail = PlaceholderEmails.Visible(customer?.Email),
                GuestPhone = customer?.PhoneNumber,
                CheckIn = stay.CheckIn,
                CheckOut = stay.CheckOut,
                Nights = stay.Nights,
                Status = stay.Status,
                Rooms = stay.Rooms,
                Balance = stay.Balance,
                Source = stay.Source,
                Tours = during,
                Url = stay.Url,
            };
        }).ToList();
        return new(rows, [.. asked.Select(h => h.Hostel.Name)], [.. asked.Where(h => h.Failed).Select(h => h.Hostel.Name)]);
    }
}

internal sealed class SearchHostelBookingsHandler(HostelBookings bookings) : IQueryHandler<SearchHostelBookings, PagedResult<HostelBookingRow>>
{
    public async ValueTask<PagedResult<HostelBookingRow>> Handle(SearchHostelBookings query, CancellationToken cancellationToken) =>
        Matching((await bookings.GetAsync(query.View, query.CheckInFrom, query.CheckInTo, cancellationToken)).Rows, query).ToPage(query);

    /// <summary>The view's reservations the filters and search keep, sorted as asked (the table and its export).</summary>
    public static IQueryable<HostelBookingRow> Matching(IEnumerable<HostelBookingRow> all, SearchHostelBookings query)
    {
        var keyword = query.Keyword?.Trim();
        var rows = all
            .Where(r => string.IsNullOrWhiteSpace(query.Hostel) || r.Hostel.Equals(query.Hostel, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrWhiteSpace(query.Source) || string.Equals(r.Source?.Trim(), query.Source.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(r => !query.BalanceDue || r.Balance > 0)
            .Where(r => string.IsNullOrWhiteSpace(keyword)
                || r.GuestName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || r.ReservationId.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || (r.Rooms?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.GuestEmail?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false));
        // In house: leaving soonest first. Otherwise: arriving soonest first.
        return rows.AsQueryable()
            .OrderBy(query.OrderBy, query.View == HostelBookingsView.InHouse ? r => r.CheckOut : r => r.CheckIn);
    }
}

/// <summary>Every reservation the table would show for the same view, filters, search and sort, one row each.</summary>
internal sealed class ExportHostelBookingsHandler(HostelBookings bookings, ISpreadsheetWriter spreadsheets, IBusinessClock clock)
    : IQueryHandler<ExportHostelBookings, FileDownload>
{
    public async ValueTask<FileDownload> Handle(ExportHostelBookings query, CancellationToken cancellationToken)
    {
        var search = query.Search;
        var rows = SearchHostelBookingsHandler.Matching(
            (await bookings.GetAsync(search.View, search.CheckInFrom, search.CheckInTo, cancellationToken)).Rows, search).ToList();
        const string Day = "dd/mm/yyyy";
        return spreadsheets.Write($"Hostel Bookings {clock.Today:yyyy-MM-dd}.xlsx", rows,
        [
            new("Reservation", r => r.ReservationId), new("Guest", r => r.GuestName), new("Email", r => r.GuestEmail), new("Phone", r => r.GuestPhone),
            new("Hostel", r => r.Hostel), new("Check In", r => r.CheckIn.ToDateTime(TimeOnly.MinValue), Day),
            new("Check Out", r => r.CheckOut.ToDateTime(TimeOnly.MinValue), Day), new("Nights", r => r.Nights), new("Room", r => r.Rooms),
            new("Source", r => r.Source), new("Tours during stay", r => r.Tours), new("Balance", r => r.Balance, "#,##0.00"), new("Status", r => r.Status),
        ]);
    }
}

internal sealed class GetHostelBookingHostelsHandler(HostelBookings bookings) : IQueryHandler<GetHostelBookingHostels, HostelBookingHostels>
{
    public async ValueTask<HostelBookingHostels> Handle(GetHostelBookingHostels query, CancellationToken cancellationToken)
    {
        var snapshot = await bookings.GetAsync(query.View, query.CheckInFrom, query.CheckInTo, cancellationToken);
        return new(snapshot.Hostels, snapshot.Unreachable)
        {
            Sources = [.. snapshot.Rows.Select(r => r.Source?.Trim()).OfType<string>().Where(s => s != "")
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)],
        };
    }
}
