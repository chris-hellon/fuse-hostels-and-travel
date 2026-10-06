using Fuse.Application.Common.Cloudbeds;
using Fuse.Application.Common.Time;
using Fuse.Application.Features.Identity;
using Fuse.Contracts.Dashboard;

namespace Fuse.Application.Features.Dashboard;

/// <summary>
/// Dashboard › Sell a Tour Today: the In House, No Tours report's guests (same Cloudbeds answer, same two-minute cache),
/// narrowed to those in house now or checking in today. The counts and the table's pages all read that one snapshot.
/// </summary>
internal static class SellATourList
{
    /// <summary>Today's list (one hostel's, or all), optionally only guests whose name or room matches <paramref name="keyword"/>.</summary>
    public static async Task<(List<SellATourRow> Rows, GuestsWithoutTours.Snapshot Snapshot)> RowsAsync(GuestsWithoutTours guests,
        IBusinessClock clock, string? hostel, string? keyword, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.Today);
        var snapshot = await guests.GetAsync(cancellationToken);
        hostel = hostel?.Trim();
        keyword = keyword?.Trim();
        var rows = snapshot.Rows
            .Where(r => r.InHouse || r.Guest.CheckIn == today)
            .Where(r => string.IsNullOrEmpty(hostel) || r.Guest.Hostel.Equals(hostel, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrEmpty(keyword)
                || r.Guest.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || (r.Guest.Rooms?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(r => new SellATourRow
            {
                Name = r.Guest.Name,
                GuestId = r.Guest.GuestId,
                Hostel = r.Guest.Hostel,
                InHouse = r.InHouse,
                CheckIn = r.Guest.CheckIn,
                CheckOut = r.Guest.CheckOut,
                NightsLeft = Math.Max(0, r.Guest.CheckOut.DayNumber - Math.Max(today.DayNumber, r.Guest.CheckIn.DayNumber)),
            })
            .ToList();
        return (rows, snapshot);
    }
}

internal sealed class SearchSellATourTodayHandler(GuestsWithoutTours guests, IBusinessClock clock)
    : IQueryHandler<SearchSellATourToday, PagedResult<SellATourRow>>
{
    public async ValueTask<PagedResult<SellATourRow>> Handle(SearchSellATourToday query, CancellationToken cancellationToken)
    {
        var (rows, _) = await SellATourList.RowsAsync(guests, clock, query.Hostel, query.Keyword, cancellationToken);
        if (query.OrderBy is { Length: > 0 })
            return rows.AsQueryable().OrderBy(query.OrderBy, r => r.CheckOut).ToPage(query);
        // In house leaving soonest first (the last chance to sell them one), then today's arrivals.
        return rows
            .OrderByDescending(r => r.InHouse)
            .ThenBy(r => r.InHouse ? r.CheckOut : r.CheckIn)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .AsQueryable()
            .ToPage(query);
    }
}

internal sealed class GetSellATourSummaryHandler(GuestsWithoutTours guests, IBusinessClock clock) : IQueryHandler<GetSellATourSummary, SellATourSummary>
{
    public async ValueTask<SellATourSummary> Handle(GetSellATourSummary query, CancellationToken cancellationToken)
    {
        var (rows, snapshot) = await SellATourList.RowsAsync(guests, clock, query.Hostel, null, cancellationToken);
        return new(rows.Count(r => !r.InHouse), rows.Count(r => r.InHouse), snapshot.Hostels, snapshot.Unreachable);
    }
}
