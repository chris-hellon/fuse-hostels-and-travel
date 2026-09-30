using System.Text.Json;
using Fuse.Application.Common.Files;
using Fuse.Application.Common.Time;
using Fuse.Contracts.Reports;

namespace Fuse.Application.Features.Reports;

/// <summary>Runs GetStaffBookingsReport (tuned, shared with legacy; commission rules live there).</summary>
internal static class StaffBookingsReport
{
    /// <summary>A row of the proc; its lines come as JSON.</summary>
    private sealed record ProcRow
    {
        public DefaultIdType UserId { get; init; }
        public string? FullName { get; init; }
        public int BookingsMade { get; init; }
        public int ItemsCount { get; init; }
        public decimal TotalBookingsAmount { get; init; }
        public decimal TotalComission { get; init; }
        public int TotalCount { get; init; }
        public string? TourDetailsJson { get; init; }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The page's sort names mapped to the proc's.</summary>
    private static string OrderBy(string[]? orderBy) =>
        StoredProcedures.ProcedureOrderBy(orderBy, nameof(StaffBookingRow.FullName));

    /// <summary>The period asked for; by default (as legacy) the last month to today.</summary>
    public static (DateOnly From, DateOnly To) Period(IBusinessClock clock, DateOnly? from, DateOnly? to)
    {
        var end = to ?? DateOnly.FromDateTime(clock.Today);
        return (from ?? end.AddMonths(-1), end);
    }

    public static async Task<PagedResult<StaffBookingRow>> RunAsync(IAppDbContext db, ITenantContext tenant, DateOnly from, DateOnly to,
        DefaultIdType? locationId, string? keyword, string[]? orderBy, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var rows = await db.QueryProcedureAsync<ProcRow>("GetStaffBookingsReport", new
        {
            tenant.TenantId,
            // Whole days of booking dates (Vietnam time, as stored); .997 is the last DATETIME tick of the day.
            FromDate = from.ToDateTime(TimeOnly.MinValue),
            ToDate = to.ToDateTime(new TimeOnly(23, 59, 59, 997)),
            DestinationId = locationId,
            Search = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim(),
            PageNumber = Math.Max(1, pageNumber),
            PageSize = pageSize,
            OrderBy = OrderBy(orderBy),
        }, cancellationToken);

        return new PagedResult<StaffBookingRow>([.. rows.Select(r => new StaffBookingRow
        {
            UserId = r.UserId,
            FullName = r.FullName?.Trim() ?? "",
            BookingsMade = r.BookingsMade,
            ItemsCount = r.ItemsCount,
            TotalBookingsAmount = r.TotalBookingsAmount,
            TotalCommission = r.TotalComission,
            Lines = string.IsNullOrEmpty(r.TourDetailsJson) ? [] : JsonSerializer.Deserialize<List<StaffBookingLine>>(r.TourDetailsJson, Json) ?? [],
        })], rows.Count > 0 ? rows[0].TotalCount : 0, pageNumber, pageSize);
    }
}

internal sealed class SearchStaffBookingsHandler(IAppDbContext db, ITenantContext tenant, IBusinessClock clock)
    : IQueryHandler<SearchStaffBookings, PagedResult<StaffBookingRow>>
{
    public async ValueTask<PagedResult<StaffBookingRow>> Handle(SearchStaffBookings query, CancellationToken cancellationToken)
    {
        var (from, to) = StaffBookingsReport.Period(clock, query.From, query.To);
        return await StaffBookingsReport.RunAsync(db, tenant, from, to, query.LocationId, query.Keyword, query.OrderBy,
            query.PageNumber, Math.Clamp(query.PageSize, 1, 500), cancellationToken);
    }
}

internal sealed class ExportStaffBookingsHandler(IAppDbContext db, ITenantContext tenant, IBusinessClock clock, ISpreadsheetWriter spreadsheets)
    : IQueryHandler<ExportStaffBookings, FileDownload>
{
    /// <summary>Every staff member fits: there are far fewer than this.</summary>
    private const int Max = 10_000;

    public async ValueTask<FileDownload> Handle(ExportStaffBookings query, CancellationToken cancellationToken)
    {
        var (from, to) = StaffBookingsReport.Period(clock, query.From, query.To);
        var staff = await StaffBookingsReport.RunAsync(db, tenant, from, to, query.LocationId, query.Keyword, null, 1, Max, cancellationToken);
        // Legacy's export: a row per staff member with their totals (raw numbers), as the page lists them.
        return spreadsheets.Write("Staff Bookings.xlsx", staff.Items,
        [
            new("Staff Name", s => s.FullName),
            new("Total Bookings Made", s => s.BookingsMade),
            new("Total Items Booked", s => s.ItemsCount),
            new("Total Bookings Revenue", s => s.TotalBookingsAmount),
            new("Total Commission Amount", s => s.TotalCommission),
        ]);
    }
}
