using System.Globalization;
using Fuse.Application.Common.Files;
using Fuse.Application.Common.Time;
using Fuse.Contracts.Reports;

namespace Fuse.Application.Features.Reports;

// Legacy's GetPaymentsReportDetailed proc (never in source control) loaded every tour of every payment in the period
// for each page; this is a plain query, and the booking's details load when a row is opened.

internal static class PaymentQueries
{
    /// <summary>Payments in the period matching the search, as rows; <see cref="PaymentRow.RefundDate"/> still UTC.</summary>
    public static IQueryable<PaymentRow> Rows(IAppDbContext db, IBusinessClock clock, DateOnly? from, DateOnly? to, string? keyword)
    {
        var (first, last) = StaffBookingsReport.Period(clock, from, to);
        var start = first.ToDateTime(TimeOnly.MinValue);
        var end = last.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var rows =
            from p in db.BookingPayments.AsNoTracking()
            join b in db.Bookings.AsNoTracking() on p.BookingId equals b.Id
            where p.PaymentDate >= start && p.PaymentDate < end
            select new { p, b };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var search = keyword.Trim();
            var number = int.TryParse(search, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : (int?)null;
            var amount = decimal.TryParse(search.TrimStart('$').Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var a) ? a : (decimal?)null;
            rows = rows.Where(r => (r.p.CustomerName != null && r.p.CustomerName.Contains(search))
                || (r.p.CustomerEmail != null && r.p.CustomerEmail.Contains(search))
                || (number != null && r.b.InvoiceId == number)
                || (amount != null && r.p.Amount == amount));
        }

        return rows.Select(r => new PaymentRow
        {
            Id = r.p.Id,
            BookingId = r.b.Id,
            Reference = r.b.InvoiceId,
            PaymentDate = r.p.PaymentDate,
            BookingDate = r.b.BookingDate,
            RefundDate = r.p.Refunded ? r.p.LastModifiedOn : null,
            Amount = r.p.Amount,
            Currency = r.p.Currency,
            // A payment without a method (most older ones) is a Stripe payment, as legacy read it (the enum's default).
            Method = r.p.PaymentMethod == BookingPaymentMethod.Cash ? "Cash" : "Stripe",
            CustomerName = r.p.CustomerName,
            CustomerEmail = r.p.CustomerEmail,
            StripeSessionId = r.p.StripeSessionId,
            Refunded = r.p.Refunded,
            BookingCancelled = r.b.Cancelled == true,
        });
    }

    public static PaymentRow Local(PaymentRow row) =>
        row.RefundDate is { } utc ? row with { RefundDate = BusinessClock.FromUtc(utc) } : row;
}

internal sealed class SearchPaymentsHandler(IAppDbContext db, IBusinessClock clock) : IQueryHandler<SearchPayments, PagedResult<PaymentRow>>
{
    public async ValueTask<PagedResult<PaymentRow>> Handle(SearchPayments query, CancellationToken cancellationToken)
    {
        var page = await PaymentQueries.Rows(db, clock, query.From, query.To, query.Keyword)
            .OrderBy(query.OrderBy, p => p.PaymentDate, descending: true)
            .ToPagedResultAsync(query, cancellationToken);
        return page with { Items = [.. page.Items.Select(PaymentQueries.Local)] };
    }
}

/// <summary>Legacy's payments export (ExportPaymentsRequest over GetPaymentsReportDetailed): a row per tour booked × payment
/// of its booking, newest payment first. Amount is the tour line (its price × guests, the lead guest included), as text.</summary>
internal sealed class ExportPaymentsHandler(IAppDbContext db, IBusinessClock clock, ISpreadsheetWriter spreadsheets) : IQueryHandler<ExportPayments, FileDownload>
{
    /// <summary>A bound on one export.</summary>
    private const int Max = 50_000;

    private const string When = "dd/MM/yyyy HH:mm";
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    /// <summary>A tour booked on a paid booking (the proc's second result set).</summary>
    private sealed record Item(DefaultIdType BookingId, string TourName, DateTime StartDate, DateTime EndDate, string? PickupLocation,
        decimal Amount, string? BookingSource, string? StaffName);

    public async ValueTask<FileDownload> Handle(ExportPayments query, CancellationToken cancellationToken)
    {
        var (from, to) = StaffBookingsReport.Period(clock, query.From, query.To);
        var payments = PaymentQueries.Rows(db, clock, from, to, query.Keyword);
        var rows = (await payments.OrderByDescending(p => p.PaymentDate).Take(Max).ToListAsync(cancellationToken))
            .Select(PaymentQueries.Local).ToList();

        // Tours (not deleted, even if the tour or departure since was) of the bookings paid; who made the booking, as the
        // proc (its user, by CreatedBy - the guest themselves for a website booking).
        var bookingIds = payments.Select(p => p.BookingId);
        var items = await (
            from i in db.BookingItems.AsNoTracking()
            where bookingIds.Contains(i.BookingId)
            join d in db.TourDates.IgnoreQueryFilters() on i.TourDateId equals (DefaultIdType?)d.Id
            join t in db.Tours.IgnoreQueryFilters() on d.TourId equals t.Id
            join u in db.Users.IgnoreQueryFilters() on i.Booking.CreatedBy.ToString() equals u.Id into users
            from u in users.DefaultIfEmpty()
            orderby d.StartDate, t.Name
            select new Item(i.BookingId, t.Name, d.StartDate, d.EndDate, i.PickupLocation,
                i.Amount * (db.BookingItemGuests.Count(g => g.BookingItemId == i.Id) + 1), i.Booking.BookingSource, u.FullName))
            .ToListAsync(cancellationToken);

        // Item-major, then a stable sort by payment date, as legacy built it. (Legacy's proc listed a booking's items once
        // per payment in the period, repeating rows and doubling guests for a booking paid twice; each item appears once here.)
        var byBooking = rows.ToLookup(p => p.BookingId);
        var lines = items.SelectMany(i => byBooking[i.BookingId].Select(p => (Payment: p, Item: i)))
            .OrderByDescending(l => l.Payment.PaymentDate).ToList();

        return spreadsheets.Write("Payments.xlsx", lines,
        [
            new("Booking Id", l => l.Payment.Reference),
            new("Payment Date", l => l.Payment.PaymentDate),
            new("Booking Date", l => l.Payment.BookingDate, When),
            new("Amount", l => "$" + l.Item.Amount.ToString("n2", Us)),
            new("Customer Name", l => l.Payment.CustomerName),
            new("Customer Email", l => l.Payment.CustomerEmail),
            new("Tour", l => l.Item.TourName),
            new("Tour Start Date", l => l.Item.StartDate, When),
            new("Tour End Date", l => l.Item.EndDate, When),
            new("Pickup Location", l => l.Item.PickupLocation),
            new("Booking Source", l => l.Item.BookingSource),
            new("Booked By", l => l.Item.StaffName),
            new("Stripe Session Id", l => l.Payment.StripeSessionId),
            new("Refunded", l => l.Payment.Refunded),
            new("Refund Date", l => l.Payment.RefundDate),
        ]);
    }
}
