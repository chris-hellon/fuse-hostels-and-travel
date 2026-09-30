using Fuse.Application.Common.Files;
using Fuse.Application.Common.Time;
using Fuse.Application.Features.Bookings;
using Fuse.Application.Features.Tours;
using Fuse.Contracts.Dashboard;
using Fuse.Contracts.Tours;

namespace Fuse.Application.Features.Dashboard;

internal sealed class GetDashboardStatsHandler(IAppDbContext db, ITenantContext tenant, IBusinessClock clock)
    : IQueryHandler<GetDashboardStats, DashboardStats>
{
    /// <summary>The proc's range columns (a class: Dapper maps by name and the proc returns more columns).</summary>
    private sealed class Totals
    {
        public int TotalBookingsCount { get; init; }
        public decimal TotalBookingsRevenue { get; init; }
        public int TotalCancelledCount { get; init; }
        public decimal TotalCancelledRevenue { get; init; }
        public int TotalRefundedCount { get; init; }
        public decimal TotalRefundedRevenue { get; init; }
    }

    public async ValueTask<DashboardStats> Handle(GetDashboardStats query, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var from = (query.From ?? today.AddDays(-30)).Date;
        var to = (query.To ?? today).Date;
        if (to < from)
            (from, to) = (to, from);

        // The range totals come from the tuned GetDashboard proc. Its own "today" figures use the
        // database clock (UTC on Azure), so today's are worked out here in business time instead.
        var totals = (await db.QueryProcedureAsync<Totals>("GetDashboard", new { tenant.TenantId, FromDate = from, ToDate = to }, cancellationToken))
            .FirstOrDefault() ?? new Totals();
        var todays = await TodayAsync(today, cancellationToken);

        return new DashboardStats
        {
            TodaysBookings = todays.Bookings,
            TodaysDepartures = todays.Departures,
            TodaysRevenue = todays.Revenue,
            TotalBookings = totals.TotalBookingsCount,
            TotalRevenue = totals.TotalBookingsRevenue,
            TotalCancelled = totals.TotalCancelledCount,
            TotalCancelledValue = totals.TotalCancelledRevenue,
            TotalRefunded = totals.TotalRefundedCount,
            TotalRefundedValue = totals.TotalRefundedRevenue,
        };
    }

    /// <summary>The proc's "Today" rules: per booked tour, guests = lead + additional, value = price × guests.</summary>
    private async Task<(int Bookings, int Departures, decimal Revenue)> TodayAsync(DateTime today, CancellationToken cancellationToken)
    {
        var tomorrow = today.AddDays(1);
        var items = await db.BookingItems.AsNoTracking()
            .Where(i => i.TourId != null && i.TourDate != null
                        && ((i.Booking.BookingDate >= today && i.Booking.BookingDate < tomorrow) || (i.StartDate >= today && i.StartDate < tomorrow)))
            .Select(i => new
            {
                i.Id,
                i.Booking.BookingDate,
                i.StartDate,
                i.Booking.IsPaid,
                Cancelled = i.Booking.Cancelled == true,
                Refunded = i.Booking.Refunded == true,
                Price = i.TourDate!.TourPrice.Price,
            })
            .ToListAsync(cancellationToken);
        if (items.Count == 0)
            return (0, 0, 0);

        var ids = items.Select(i => i.Id).ToList();
        var additional = await db.BookingItemGuests.AsNoTracking().Where(g => ids.Contains(g.BookingItemId))
            .GroupBy(g => g.BookingItemId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        var live = items.Where(i => !i.Cancelled && !i.Refunded).ToList();
        var bookedToday = live.Where(i => i.BookingDate >= today && i.BookingDate < tomorrow).ToList();
        int Guests(DefaultIdType id) => 1 + additional.GetValueOrDefault(id);

        return (
            bookedToday.Count,
            live.Where(i => i.StartDate >= today && i.StartDate < tomorrow).Sum(i => Guests(i.Id)),
            bookedToday.Where(i => i.IsPaid).Sum(i => i.Price * Guests(i.Id)));
    }
}

internal sealed class SearchDeparturesHandler(IAppDbContext db, ITenantContext tenant, SupplierScope scope, IBusinessClock clock)
    : IQueryHandler<SearchDepartures, PagedResult<DepartureRow>>
{
    public async ValueTask<PagedResult<DepartureRow>> Handle(SearchDepartures query, CancellationToken cancellationToken)
    {
        var day = clock.Today.AddDays(query.DaysFromToday);
        var rows = await BookingExports.QueryAsync(db, tenant, scope, new BookingExports.Filter
        {
            TourStart = day,
            TourEnd = day.AddDays(1).AddSeconds(-1),
            TourId = query.TourId,
            Keyword = query.Keyword,
        }, cancellationToken);

        return rows.Select(r => r.ToDeparture()).AsQueryable()
            .OrderBy(query.OrderBy, r => r.StartDate)
            .ToPage(query);
    }
}

internal sealed class ExportDeparturesHandler(IAppDbContext db, ITenantContext tenant, SupplierScope scope, ISpreadsheetWriter spreadsheets, IBusinessClock clock)
    : IQueryHandler<ExportDepartures, FileDownload>
{
    public async ValueTask<FileDownload> Handle(ExportDepartures query, CancellationToken cancellationToken)
    {
        var day = clock.Today.AddDays(query.DaysFromToday);
        var rows = await BookingExports.QueryAsync(db, tenant, scope, new BookingExports.Filter
        {
            TourStart = day,
            TourEnd = day.AddDays(1).AddSeconds(-1),
            TourId = query.TourId,
            Keyword = query.Keyword,
        }, cancellationToken);
        return spreadsheets.Write($"Departures {day:yyyy-MM-dd}.xlsx", rows, BookingExports.Columns);
    }
}

internal sealed class SearchTodaysBookingsHandler(IAppDbContext db, IBusinessClock clock, SupplierScope scope)
    : IQueryHandler<SearchTodaysBookings, PagedResult<BookingActivityRow>>
{
    public async ValueTask<PagedResult<BookingActivityRow>> Handle(SearchTodaysBookings query, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var tomorrow = today.AddDays(1);
        var tours = await scope.TourIdsAsync(cancellationToken);

        var bookings = db.Bookings.AsNoTracking()
            .Where(b => b.BookingDate >= today && b.BookingDate < tomorrow && b.Items.Any(i => i.TourId != null));
        if (query.TourId is { } tourId)
            bookings = bookings.Where(b => b.Items.Any(i => i.TourId == tourId));
        if (tours is not null)
            bookings = bookings.Where(b => b.Items.Any(i => i.TourId != null && tours.Contains(i.TourId.Value)));
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            bookings = bookings.Where(b => b.Description.Contains(query.Keyword) || (b.GuestName != null && b.GuestName.Contains(query.Keyword)));

        return await bookings
            .Select(b => new BookingActivityRow
            {
                Id = b.Id,
                Reference = b.InvoiceId,
                Description = b.Description,
                GuestId = b.GuestId,
                GuestName = b.GuestName,
                BookingDate = b.BookingDate,
                TotalAmount = b.TotalAmount,
                IsPaid = b.IsPaid,
                AmountOutstanding = b.AmountOutstanding,
                Refunded = b.Refunded,
                Cancelled = b.Cancelled,
            })
            .OrderBy(query.OrderBy, b => b.BookingDate, descending: true)
            .ToPagedResultAsync(query, cancellationToken);
    }
}

internal sealed class ListDashboardToursHandler(IAppDbContext db, SupplierScope scope) : IQueryHandler<ListDashboardTours, IReadOnlyList<LookupItem>>
{
    public async ValueTask<IReadOnlyList<LookupItem>> Handle(ListDashboardTours query, CancellationToken cancellationToken)
    {
        var tours = await scope.TourIdsAsync(cancellationToken);
        var list = db.Tours.AsNoTracking();
        if (tours is not null)
            list = list.Where(t => tours.Contains(t.Id));
        return (await list.OrderBy(t => t.Name).Select(t => new { t.Id, t.Name }).ToListAsync(cancellationToken)).Select(t => new LookupItem(t.Id.ToString(), t.Name)).ToList();
    }
}
