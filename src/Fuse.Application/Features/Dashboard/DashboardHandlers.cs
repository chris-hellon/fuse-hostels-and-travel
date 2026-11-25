using Fuse.Application.Common.Files;
using Fuse.Application.Common.Time;
using Fuse.Application.Features.Bookings;
using Fuse.Application.Features.Tours;
using Fuse.Contracts.Dashboard;
using Fuse.Contracts.Tours;

namespace Fuse.Application.Features.Dashboard;

internal sealed class GetDashboardStatsHandler(IAppDbContext db, IBusinessClock clock)
    : IQueryHandler<GetDashboardStats, DashboardStats>
{
    public async ValueTask<DashboardStats> Handle(GetDashboardStats query, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var from = (query.From ?? today.AddDays(-30)).Date;
        var to = (query.To ?? today).Date;
        if (to < from)
            (from, to) = (to, from);

        var totals = await RangeAsync(from, to.AddDays(1), cancellationToken);
        var todays = await TodayAsync(today, cancellationToken);

        return new DashboardStats
        {
            TodaysBookings = todays.Bookings,
            TodaysDepartures = todays.Departures,
            TodaysRevenue = todays.Revenue,
            TotalBookings = totals.Count(i => !i.Cancelled && !i.Refunded),
            TotalRevenue = totals.Where(i => i.IsPaid && !i.Refunded).Sum(i => i.Value),
            TotalCancelled = totals.Count(i => i.Cancelled),
            TotalCancelledValue = totals.Where(i => i.IsPaid && i.Cancelled).Sum(i => i.Value),
            TotalRefunded = totals.Count(i => i.Refunded),
            TotalRefundedValue = totals.Where(i => i.Refunded).Sum(i => i.Value),
        };
    }

    private sealed record Item(bool IsPaid, bool Cancelled, bool Refunded, decimal Value);

    /// <summary>The soft-delete query filter (AppDbContext.SoftDeleteFilter), lifted where the procedure didn't apply it.</summary>
    private static readonly string[] SoftDelete = ["SoftDelete"];

    /// <summary>
    /// The range totals, by the GetDashboard procedure's rules (each booked tour; its value = price × (lead + additional
    /// guests), additional guests counted whether removed or not, as the procedure did). Only the range's bookings are
    /// read (by booking date, an index), not every booking ever made as the procedure did, which took seconds on the live
    /// database and slowed the rest of the dashboard with it.
    /// </summary>
    private async Task<IReadOnlyList<Item>> RangeAsync(DateTime from, DateTime toExclusive, CancellationToken cancellationToken)
    {
        // Soft deletes by hand, as the procedure: deleted bookings and tours are left out, but a deleted departure or
        // price (which it joined regardless) still counts.
        var rows = await db.BookingItems.IgnoreQueryFilters(SoftDelete).AsNoTracking()
            .Where(i => i.DeletedOn == null && i.Booking.DeletedOn == null && i.TourId != null && i.TourDate != null && i.TourDate.TourPrice != null
                        && i.Booking.BookingDate >= from && i.Booking.BookingDate < toExclusive)
            .Select(i => new
            {
                i.Booking.IsPaid,
                Cancelled = i.Booking.Cancelled == true,
                Refunded = i.Booking.Refunded == true,
                Price = i.TourDate!.TourPrice!.Price,
                Additional = db.BookingItemGuests.IgnoreQueryFilters(SoftDelete).Count(g => g.BookingItemId == i.Id),
            })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new Item(r.IsPaid, r.Cancelled, r.Refunded, r.Price * (1 + r.Additional)))];
    }

    /// <summary>The proc's "Today" rules: per booked tour, guests = lead + additional, value = price × guests.</summary>
    private async Task<(int Bookings, int Departures, decimal Revenue)> TodayAsync(DateTime today, CancellationToken cancellationToken)
    {
        var tomorrow = today.AddDays(1);
        // Booked today, and departing today, in one round trip (UNION ALL): two halves that can each use an index,
        // which one OR across both tables can't.
        var live = db.BookingItems.AsNoTracking().Where(i => i.TourId != null && i.TourDate != null && i.Booking.Cancelled != true && i.Booking.Refunded != true);
        var rows = await live.Where(i => i.Booking.BookingDate >= today && i.Booking.BookingDate < tomorrow)
            .Select(i => new { Booked = true, i.Id, i.Booking.IsPaid, Price = i.TourDate!.TourPrice.Price, Guests = 1 + i.Guests!.Count() })
            .Concat(live.Where(i => i.StartDate >= today && i.StartDate < tomorrow)
                .Select(i => new { Booked = false, i.Id, i.Booking.IsPaid, Price = i.TourDate!.TourPrice.Price, Guests = 1 + i.Guests!.Count() }))
            .ToListAsync(cancellationToken);

        var booked = rows.Where(r => r.Booked).ToList();
        return (
            booked.Count,
            rows.Where(r => !r.Booked).Sum(r => r.Guests),
            booked.Where(r => r.IsPaid).Sum(r => r.Price * r.Guests));
    }
}

internal sealed class SearchDeparturesHandler(IAppDbContext db, ITenantContext tenant, SupplierScope scope, IBusinessClock clock)
    : IQueryHandler<SearchDepartures, PagedResult<DepartureRow>>
{
    /// <summary>A GetDashboardDepartures row: one person on a departure, and what decides their status.</summary>
    private sealed class Row
    {
        public int Reference { get; init; }
        public string? TourName { get; init; }
        public string? GuestId { get; init; }
        public string? GuestName { get; init; }
        public DateTime StartDate { get; init; }
        public DateTime EndDate { get; init; }
        public string? PickupLocation { get; init; }
        public bool? WaiverSigned { get; init; }
        public bool? Cancelled { get; init; }
        public bool? NoShow { get; init; }
        public bool? CheckedIn { get; init; }
        public bool? BookingRefunded { get; init; }
        public bool? TourRefunded { get; init; }
        public bool IsPaid { get; init; }

        // As the export's Status (BookingExportRow.Status).
        public DepartureRow ToDeparture() => new()
        {
            Reference = Reference,
            TourName = TourName ?? "",
            GuestId = GuestId,
            GuestName = GuestName,
            StartDate = StartDate,
            EndDate = EndDate,
            PickupLocation = PickupLocation,
            WaiverSigned = WaiverSigned == true,
            Status = Cancelled == true ? BookingState.Cancelled
                : NoShow == true ? BookingState.NoShow
                : CheckedIn == true ? BookingState.CheckedIn
                : BookingRefunded == true || TourRefunded == true ? BookingState.Refunded
                : IsPaid ? BookingState.Paid
                : BookingState.Unpaid,
        };
    }

    public async ValueTask<PagedResult<DepartureRow>> Handle(SearchDepartures query, CancellationToken cancellationToken)
    {
        var tours = await scope.TourIdsAsync(cancellationToken);
        if (tours is { Count: 0 })
            return PagedResult<DepartureRow>.Empty(query.PageSize);

        var day = clock.Today.AddDays(query.DaysFromToday);
        // Its own small procedure (the table's columns only): the bookings export one took seconds on the live database.
        var rows = await db.QueryProcedureAsync<Row>("GetDashboardDepartures", new
        {
            tenant.TenantId,
            FromDate = day,
            ToDate = day.AddDays(1),
            query.TourId,
            Search = string.IsNullOrWhiteSpace(query.Keyword) ? null : query.Keyword,
            TourIds = StoredProcedures.GuidList(tours ?? []),
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
