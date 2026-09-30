using Fuse.Application.Common.Files;
using Fuse.Application.Common.Time;
using Fuse.Application.Features.Bookings;
using Fuse.Application.Features.Tours;
using Fuse.Contracts.Dashboard;
using Fuse.Contracts.Manifest;
using Fuse.Contracts.Tours;

namespace Fuse.Application.Features.Manifest;

/// <summary>A row of the GetTodaysTourManifest proc (tuned, shared with legacy).</summary>
internal sealed class ManifestProcRow
{
    public string BookingInvoiceId { get; init; } = "";
    public DefaultIdType TourId { get; init; }
    public string TourName { get; init; } = "";
    public DefaultIdType? TourDateId { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public DefaultIdType GuestId { get; init; }
    public string GuestName { get; init; } = "";
    public bool? BookingRefunded { get; init; }
    public bool? TourRefunded { get; init; }
    public bool BookingIsPaid { get; init; }
    public bool? WaiverSigned { get; init; }
    public bool? Cancelled { get; init; }
    public bool? NoShow { get; init; }
    public bool? CheckedIn { get; init; }
    public int TotalCount { get; init; }

    public ManifestRow ToRow() => new()
    {
        Reference = int.TryParse(BookingInvoiceId, out var reference) ? reference : 0,
        TourId = TourId,
        TourName = TourName,
        TourDateId = TourDateId,
        StartDate = StartDate,
        EndDate = EndDate,
        GuestId = GuestId.ToString(),
        GuestName = GuestName,
        WaiverSigned = WaiverSigned == true,
        Status = Cancelled == true ? BookingState.Cancelled
            : NoShow == true ? BookingState.NoShow
            : CheckedIn == true ? BookingState.CheckedIn
            : BookingRefunded == true || TourRefunded == true ? BookingState.Refunded
            : BookingIsPaid ? BookingState.Paid
            : BookingState.Unpaid,
    };
}

internal static class ManifestWindow
{
    /// <summary>Today, or just the departures at <paramref name="time"/> today.</summary>
    public static (DateTime From, DateTime To) For(DateTime today, TimeOnly? time) =>
        time is { } t ? (today + t.ToTimeSpan(), today + t.ToTimeSpan()) : (today, today.AddDays(1).AddSeconds(-1));

    /// <summary>The page's sort names mapped to the proc's.</summary>
    public static string OrderBy(string[]? orderBy)
    {
        var sort = StoredProcedures.ProcedureOrderBy(orderBy, nameof(ManifestRow.StartDate));
        return sort.Replace(nameof(ManifestRow.WaiverSigned), "BookingWaiverSigned").Replace(nameof(ManifestRow.Status), "BookingIsPaid");
    }
}

internal sealed class SearchManifestHandler(IAppDbContext db, ITenantContext tenant, IBusinessClock clock, SupplierScope scope)
    : IQueryHandler<SearchManifest, PagedResult<ManifestRow>>
{
    public async ValueTask<PagedResult<ManifestRow>> Handle(SearchManifest query, CancellationToken cancellationToken)
    {
        // The proc can't filter by a list of tours, so a supplier must pick one of theirs.
        var tours = await scope.TourIdsAsync(cancellationToken);
        if (tours is not null && (query.TourId is not { } chosen || !tours.Contains(chosen)))
            return new PagedResult<ManifestRow>([], 0, query.PageNumber, query.PageSize);

        var (from, to) = ManifestWindow.For(clock.Today, query.StartTime);
        var rows = await db.QueryProcedureAsync<ManifestProcRow>("GetTodaysTourManifest", new
        {
            tenant.TenantId,
            query.TourId,
            TourStartDate = from,
            TourEndDate = to,
            Description = string.IsNullOrWhiteSpace(query.Keyword) ? null : query.Keyword,
            PageNumber = Math.Max(1, query.PageNumber),
            PageSize = Math.Clamp(query.PageSize, 1, 500),
            OrderBy = ManifestWindow.OrderBy(query.OrderBy),
        }, cancellationToken);

        return new PagedResult<ManifestRow>([.. rows.Select(r => r.ToRow())], rows.Count > 0 ? rows[0].TotalCount : 0, query.PageNumber, query.PageSize);
    }
}

internal sealed class ListManifestToursHandler(IAppDbContext db, IBusinessClock clock, SupplierScope scope)
    : IQueryHandler<ListManifestTours, IReadOnlyList<LookupItem>>
{
    public async ValueTask<IReadOnlyList<LookupItem>> Handle(ListManifestTours query, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var tomorrow = today.AddDays(1);
        var allowed = await scope.TourIdsAsync(cancellationToken);
        var ids = db.BookingItems.AsNoTracking()
            .Where(i => i.TourId != null && i.StartDate >= today && i.StartDate < tomorrow && i.Booking.IsPaid)
            .Select(i => i.TourId!.Value);
        var tours = db.Tours.AsNoTracking().Where(t => ids.Contains(t.Id));
        if (allowed is not null)
            tours = tours.Where(t => allowed.Contains(t.Id));
        return (await tours.OrderBy(t => t.Name).Select(t => new { t.Id, t.Name }).ToListAsync(cancellationToken)).Select(t => new LookupItem(t.Id.ToString(), t.Name)).ToList();
    }
}

internal sealed class ListManifestTimesHandler(IAppDbContext db, IBusinessClock clock) : IQueryHandler<ListManifestTimes, IReadOnlyList<TimeOnly>>
{
    public async ValueTask<IReadOnlyList<TimeOnly>> Handle(ListManifestTimes query, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var tomorrow = today.AddDays(1);
        var starts = await db.BookingItems.AsNoTracking()
            .Where(i => i.TourId == query.TourId && i.StartDate >= today && i.StartDate < tomorrow)
            .Select(i => i.StartDate).Distinct().ToListAsync(cancellationToken);
        return [.. starts.Select(TimeOnly.FromDateTime).Distinct().Order()];
    }
}

internal sealed class SetGuestAttendanceHandler(IAppDbContext db, SupplierScope scope) : ICommandHandler<SetGuestAttendance>
{
    public async ValueTask<Unit> Handle(SetGuestAttendance command, CancellationToken cancellationToken)
    {
        var tours = await scope.TourIdsAsync(cancellationToken);
        if (tours is not null && !tours.Contains(command.TourId))
            throw new NotFoundException("Booking not found.");

        // As the legacy proc: the booked tour is found by the departure (or the tour and its start),
        // then the guest is an additional traveller if listed, otherwise the lead guest (the item itself).
        var item = await db.BookingItems
            .Where(i => i.Booking.InvoiceId == command.Reference && i.TourId == command.TourId
                        && (command.TourDateId != null ? i.TourDateId == command.TourDateId : i.StartDate == command.StartDate))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Booking not found.");

        var (checkedIn, noShow, cancelled) = command.Attendance switch
        {
            GuestAttendance.CheckedIn => (true, false, false),
            GuestAttendance.NoShow => (false, true, false),
            _ => (false, false, true),
        };

        var guestId = DefaultIdType.TryParse(command.GuestId, out var id) ? id : DefaultIdType.Empty;
        var guest = await db.BookingItemGuests.FirstOrDefaultAsync(g => g.BookingItemId == item.Id && g.GuestId == guestId, cancellationToken);
        if (guest is not null)
            guest.SetAttendance(checkedIn, noShow, cancelled);
        else
            item.SetAttendance(checkedIn, noShow, cancelled);

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class ExportManifestHandler(IAppDbContext db, ITenantContext tenant, SupplierScope scope, ISpreadsheetWriter spreadsheets, IBusinessClock clock)
    : IQueryHandler<ExportManifest, FileDownload>
{
    public async ValueTask<FileDownload> Handle(ExportManifest query, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var (from, to) = ManifestWindow.For(today, query.StartTime);
        var rows = await BookingExports.QueryAsync(db, tenant, scope, new BookingExports.Filter
        {
            TourStart = from,
            TourEnd = to,
            TourId = query.TourId,
            Keyword = query.Keyword,
            PaidOnly = true,
        }, cancellationToken);
        return spreadsheets.Write($"Manifest {today:yyyy-MM-dd}.xlsx", rows, BookingExports.Columns);
    }
}
