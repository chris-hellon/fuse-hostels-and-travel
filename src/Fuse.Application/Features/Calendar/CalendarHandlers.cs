using Fuse.Application.Common.Files;
using Fuse.Application.Features.Bookings;
using Fuse.Application.Features.Tours;
using Fuse.Contracts.Calendar;

namespace Fuse.Application.Features.Calendar;

internal sealed class ListCalendarDeparturesHandler(IAppDbContext db, ITenantContext tenant, SupplierScope scope)
    : IQueryHandler<ListCalendarDepartures, IReadOnlyList<CalendarDeparture>>
{
    /// <summary>The calendar asks for a month view (about six weeks); never more than this.</summary>
    private const int MaxDays = 100;

    private sealed class Row
    {
        public string Tour { get; init; } = "";
        public DefaultIdType TourId { get; init; }
        public DateTime StartDate { get; init; }
        public DateTime EndDate { get; init; }
        public int GuestCount { get; init; }
    }

    public async ValueTask<IReadOnlyList<CalendarDeparture>> Handle(ListCalendarDepartures query, CancellationToken cancellationToken)
    {
        var from = query.From.Date;
        var to = query.To.Date;
        if (to < from || (to - from).TotalDays > MaxDays)
            throw new BusinessRuleException($"Choose a range of up to {MaxDays} days.");

        var tours = await scope.TourIdsAsync(cancellationToken);
        if (tours is { Count: 0 })
            return [];

        var rows = await db.QueryProcedureAsync<Row>("GetCalendar", new
        {
            tenant.TenantId,
            FromDate = from,
            ToDate = to.AddDays(1).AddSeconds(-1),
            TourIds = StoredProcedures.GuidList(tours ?? []),
        }, cancellationToken);
        var tourIds = rows.Select(r => r.TourId).Distinct().ToList();
        var categories = await db.Tours.AsNoTracking().IgnoreQueryFilters()
            .Where(t => tourIds.Contains(t.Id) && t.TourCategory != null)
            .Select(t => new { t.Id, t.TourCategory!.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        return [.. rows.Select(r => new CalendarDeparture(r.TourId, r.Tour, r.StartDate, r.EndDate, r.GuestCount, categories.GetValueOrDefault(r.TourId)))];
    }
}

internal sealed class ListDepartureGuestsHandler(IAppDbContext db, ITenantContext tenant, SupplierScope scope)
    : IQueryHandler<ListDepartureGuests, IReadOnlyList<DepartureGuest>>
{
    private sealed class Row
    {
        public string? GuestId { get; init; }
        public int Reference { get; init; }
        public string? GuestName { get; init; }
        public bool? WaiverSigned { get; init; }
        public string? PickupLocation { get; init; }
    }

    public async ValueTask<IReadOnlyList<DepartureGuest>> Handle(ListDepartureGuests query, CancellationToken cancellationToken)
    {
        var tours = await scope.TourIdsAsync(cancellationToken);
        if (tours is not null && !tours.Contains(query.TourId))
            return [];

        // Exactly the departure's start time (the proc's window is inclusive at both ends).
        var rows = await db.QueryProcedureAsync<Row>("GetCalendarItems", new
        {
            tenant.TenantId,
            TourStartDate = query.Start,
            TourEndDate = query.Start,
            query.TourId,
            TourIds = StoredProcedures.GuidList([]),
            PaidOnly = true,
        }, cancellationToken);
        var references = rows.Select(r => r.Reference).Distinct().ToList();
        var bookings = (await db.Bookings.AsNoTracking()
                .Where(b => references.Contains(b.InvoiceId))
                .Select(b => new { b.InvoiceId, b.Id })
                .ToListAsync(cancellationToken))
            .GroupBy(b => b.InvoiceId).ToDictionary(g => g.Key, g => g.First().Id);
        return [.. rows.Select(r => new DepartureGuest(r.GuestId, r.Reference, r.GuestName, r.WaiverSigned == true, r.PickupLocation,
            bookings.TryGetValue(r.Reference, out var id) ? id : null))];
    }
}

internal sealed class ExportDepartureGuestsHandler(IAppDbContext db, ITenantContext tenant, SupplierScope scope, ISpreadsheetWriter spreadsheets)
    : IQueryHandler<ExportDepartureGuests, FileDownload>
{
    public async ValueTask<FileDownload> Handle(ExportDepartureGuests query, CancellationToken cancellationToken)
    {
        var tours = await scope.TourIdsAsync(cancellationToken);
        if (tours is not null && !tours.Contains(query.TourId))
            throw new NotFoundException("Departure not found.");

        var rows = await BookingExports.QueryAsync(db, tenant, scope, new BookingExports.Filter
        {
            TourStart = query.Start,
            TourEnd = query.Start,
            TourId = query.TourId,
            PaidOnly = true,
        }, cancellationToken);
        var tourName = rows.FirstOrDefault()?.TourName ?? "Departure";
        return spreadsheets.Write($"{tourName} {query.Start:yyyy-MM-dd HHmm}.xlsx", rows, BookingExports.Columns);
    }
}
