using Fuse.Application.Common.Files;
using Fuse.Application.Common.Jobs;
using Fuse.Application.Common.Mail;
using Fuse.Application.Common.Time;
using Fuse.Application.Features.Bookings;
using Fuse.Application.Features.Tours;

namespace Fuse.Application.Features.Manifest;

/// <summary>
/// A row of legacy's GetTodaysTourDatesForManifest proc: one TourDates row (a departure's price row) in one of its
/// tour's locations. Stored with its scheduled job, so plain data.
/// </summary>
public sealed record TourManifestDeparture(DefaultIdType TourId, DefaultIdType TourDateId, DefaultIdType DestinationId, string TourName, DateTime StartDate);

/// <summary>
/// Legacy's "Tours Daily Manifest", as it did it: at midnight, each of today's TourDates rows is scheduled once per
/// location of its tour, for 1 hour before it leaves (none for those leaving before 01:00). Then that row's guests
/// go to the hostels in that location, one email each, if it has any guests. Nothing is re-checked: a TourDates row
/// added after midnight, or first booked after its send, gets no manifest.
/// </summary>
internal sealed class TourManifestEmails(IAppDbContext db, ITenantContext tenant, SupplierScope scope, IBusinessClock clock,
    ISpreadsheetWriter spreadsheets, IBookingEmails emails, IBackgroundJobs jobs, ILogger<TourManifestEmails> logger)
{
    public static readonly TimeSpan Before = TimeSpan.FromHours(1);

    /// <summary>Today's departures and how long until each is sent; those whose time has passed are left out.</summary>
    public async Task<IReadOnlyList<(TourManifestDeparture Departure, TimeSpan Delay)>> TodaysAsync(CancellationToken cancellationToken)
    {
        var now = clock.Now;
        var rows = await db.QueryProcedureAsync<DepartureRow>("GetTodaysTourDatesForManifest", new
        {
            tenant.TenantId,
            CurrentDate = now,
        }, cancellationToken);
        return [.. rows
            .Select(r => (Departure: new TourManifestDeparture(r.TourId, r.TourDateId, r.DestinationId, r.TourName ?? "", r.StartDate), Delay: r.StartDate - Before - now))
            .Where(d => d.Delay >= TimeSpan.Zero)];
    }

    /// <summary>Sends <paramref name="departure"/>'s manifest to its location's hostels; false when there's no one to send.</summary>
    public async Task<bool> SendAsync(TourManifestDeparture departure, CancellationToken cancellationToken)
    {
        var rows = await BookingExports.QueryAsync(db, tenant, scope, new BookingExports.Filter
        {
            TourId = departure.TourId,
            TourDateId = departure.TourDateId,
            HideRefunded = true,
        }, cancellationToken);
        if (rows.Count == 0)
        {
            logger.LogInformation("Manifest: no bookings for {Tour} {Start:yyyy-MM-dd HH:mm}", departure.TourName, departure.StartDate);
            return false;
        }

        var recipients = (await db.QueryProcedureAsync<Hostel>("GetDestinationProperties", new
            {
                tenant.TenantId,
                departure.DestinationId,
            }, cancellationToken))
            .Where(p => !string.IsNullOrWhiteSpace(p.EmailAddress))
            .Select(p => p.EmailAddress!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (recipients.Count == 0)
        {
            logger.LogInformation("Manifest: no recipient emails for {Tour} in location {Location}", departure.TourName, departure.DestinationId);
            return false;
        }

        var paid = rows.Count(r => r.BookingIsPaid);
        var file = spreadsheets.Write($"{departure.TourName} {departure.StartDate:HH-mm}.xlsx", rows, BookingExports.Columns);
        var manifest = new TourManifest(departure.TourName, departure.StartDate, rows.Count, paid, rows.Count - paid, file);
        foreach (var to in recipients)
            jobs.EnqueueEmail(await emails.TourManifestAsync(manifest, to) with { Automated = true });
        logger.LogInformation("Manifest for {Tour} at {Start} ({Guests} guests) sent to {Recipients}", departure.TourName, departure.StartDate, rows.Count, string.Join(", ", recipients));
        return true;
    }

    private sealed class DepartureRow
    {
        public DefaultIdType DestinationId { get; init; }
        public DefaultIdType TourId { get; init; }
        public string? TourName { get; init; }
        public DefaultIdType TourDateId { get; init; }
        public DateTime StartDate { get; init; }
    }

    /// <summary>The column of GetDestinationProperties' rows used here.</summary>
    private sealed class Hostel
    {
        public string? EmailAddress { get; init; }
    }
}
