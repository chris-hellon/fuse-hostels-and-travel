using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Contracts.Api;
using Fuse.Contracts.Bookings;
using Fuse.Contracts.Common;
using Fuse.Contracts.Reports;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Enums;

namespace Fuse.Web.Tests;

/// <summary>Reports › Payments: payments taken in a period, searchable and exportable.</summary>
public sealed class PaymentsReportTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Today => DateOnly.FromDateTime(TestBookings.BusinessNow);

    /// <summary>An unpaid $50 booking paid in cash now; its number and id.</summary>
    private async Task<(int Reference, DefaultIdType BookingId)> CashPaymentAsync(IApi api)
    {
        var start = TestBookings.BusinessNow.Date.AddDays(3).AddHours(9);
        var (tourId, departureId) = await TestBookings.TourWithDepartureAsync(app, api, start, Ct);
        var reference = await TestBookings.BookAsync(app, tourId, departureId, start, TestBookings.BusinessNow, Ct, paid: false);
        await using var scope = TestBookings.TenantScope(app);
        var bookingId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.Where(b => b.InvoiceId == reference).Select(b => b.Id).SingleAsync(Ct);
        await api.Send(new AddBookingPayment(bookingId, 50, BookingPaymentMethod.Cash, null), Ct);
        return (reference, bookingId);
    }

    // A booking number can also be an amount ("50" finds booking 50 and $50 payments), so rows are picked by number.
    [Fact]
    public async Task A_payment_in_the_period_is_listed_found_by_booking_number_and_exported()
    {
        var api = await app.AdminApiAsync();
        var (reference, bookingId) = await CashPaymentAsync(api);

        var row = (await api.Send(new SearchPayments { From = Today, To = Today, Keyword = reference.ToString() }, Ct)).Items.Single(r => r.Reference == reference);

        (row.BookingId, row.Reference, row.Amount, row.Method, row.CustomerName, row.Refunded, row.RefundDate).ShouldBe((bookingId, reference, 50m, "Cash", "Test Guest", false, (DateTime?)null));
        row.PaymentDate.ShouldNotBeNull().Date.ShouldBe(TestBookings.BusinessNow.Date);
        // The default period (the last month to today) has it too; one before it doesn't.
        (await api.Send(new SearchPayments { Keyword = reference.ToString() }, Ct)).Items.Single(r => r.Reference == reference);
        (await api.Send(new SearchPayments { From = Today.AddDays(-10), To = Today.AddDays(-1), Keyword = reference.ToString() }, Ct)).Items.ShouldNotContain(r => r.Reference == reference);

        var file = await api.Send(new ExportPayments(Today, Today, reference.ToString()), Ct);
        file.FileName.ShouldBe("Payments.xlsx");
        file.Content.Length.ShouldBeGreaterThan(0);
    }

    // Legacy's export (GetPaymentsReportDetailed): a row per tour booked × payment of the booking, the tour line's amount as
    // text, the tour's details, and legacy's columns, sheet and table.
    [Fact]
    public async Task The_export_has_a_row_per_tour_and_payment_in_legacys_columns()
    {
        var api = await app.AdminApiAsync();
        var (reference, bookingId) = await CashPaymentAsync(api);
        var start = TestBookings.BusinessNow.Date.AddDays(3).AddHours(9);
        string tourName;
        await using (var scope = TestBookings.TenantScope(app))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // A second payment with no method recorded (as most older payments): a second row for the same tour, and "Stripe".
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO Catalog.BookingPayments (Id, BookingId, Amount, StripeSessionId, Refunded, CreatedBy, CreatedOn, LastModifiedBy, LastModifiedOn,
                    Currency, CustomerEmail, CustomerName, PaymentDate, PartialRefund, PaymentMethod)
                SELECT NEWID(), BookingId, 10, 'cs_test_second', 0, CreatedBy, CreatedOn, LastModifiedBy, LastModifiedOn,
                    Currency, CustomerEmail, CustomerName, PaymentDate, PartialRefund, NULL
                FROM Catalog.BookingPayments WHERE BookingId = {0}
                """, new object[] { bookingId }, Ct);
            tourName = await db.BookingItems.Where(i => i.BookingId == bookingId).Select(i => i.Tour!.Name).SingleAsync(Ct);
        }

        var methods = (await api.Send(new SearchPayments { From = Today, To = Today, Keyword = reference.ToString() }, Ct)).Items
            .Where(r => r.Reference == reference).Select(r => r.Method);
        methods.ShouldBe(["Cash", "Stripe"], ignoreOrder: true);

        var file = await api.Send(new ExportPayments(Today, Today, reference.ToString()), Ct);

        file.FileName.ShouldBe("Payments.xlsx");
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var sheet = workbook.Worksheet("table");
        sheet.Tables.ShouldHaveSingleItem().Name.ShouldBe("table");
        string[] headers = ["Booking Id", "Payment Date", "Booking Date", "Amount", "Customer Name", "Customer Email", "Tour", "Tour Start Date",
            "Tour End Date", "Pickup Location", "Booking Source", "Booked By", "Stripe Session Id", "Refunded", "Refund Date"];
        Enumerable.Range(1, headers.Length).Select(c => sheet.Cell(1, c).GetString()).ShouldBe(headers);
        sheet.Cell(1, headers.Length + 1).IsEmpty().ShouldBeTrue();
        var rows = sheet.RowsUsed().Skip(1).Where(r => r.Cell(1).Value.IsNumber && (int)r.Cell(1).GetDouble() == reference).ToList();
        rows.Count.ShouldBe(2); // one tour × two payments
        foreach (var row in rows)
        {
            row.Cell(4).Value.IsText.ShouldBeTrue();
            row.Cell(4).GetString().ShouldBe("$100.00"); // $50 a head × (lead + one guest), not the payment
            row.Cell(7).GetString().ShouldBe(tourName);
            row.Cell(8).GetDateTime().ShouldBe(start);
            row.Cell(8).Style.NumberFormat.Format.ShouldBe("dd/MM/yyyy HH:mm");
            row.Cell(9).GetDateTime().ShouldBe(start.AddHours(2));
            row.Cell(10).GetString().ShouldBe("Test Hostel");
            row.Cell(11).GetString().ShouldBe("Website");
            row.Cell(14).Value.IsBoolean.ShouldBeTrue();
            row.Cell(14).GetBoolean().ShouldBeFalse();
            row.Cell(15).IsEmpty().ShouldBeTrue();
        }
        rows.Select(r => r.Cell(13).GetString()).ShouldContain("cs_test_second");
    }

    [Fact]
    public async Task A_refunded_payment_shows_when_it_was_refunded_in_vietnam_time()
    {
        var api = await app.AdminApiAsync();
        var (reference, bookingId) = await CashPaymentAsync(api);
        await using (var scope = TestBookings.TenantScope(app))
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(
                "UPDATE Catalog.BookingPayments SET Refunded = 1, LastModifiedOn = '2026-01-02T03:00:00' WHERE BookingId = {0}", new object[] { bookingId }, Ct);

        var row = (await api.Send(new SearchPayments { From = Today, To = Today, Keyword = reference.ToString() }, Ct)).Items.Single(r => r.Reference == reference);

        row.Refunded.ShouldBeTrue();
        row.RefundDate.ShouldBe(new DateTime(2026, 1, 2, 10, 0, 0));
    }

    [Fact]
    public async Task The_dates_must_be_in_order()
    {
        var api = await app.AdminApiAsync();
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SearchPayments { From = Today, To = Today.AddDays(-1) }, Ct).AsTask());
    }
}
