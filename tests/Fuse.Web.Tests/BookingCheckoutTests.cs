using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Contracts.Api;
using Fuse.Contracts.Bookings;
using Fuse.Contracts.Common;
using Fuse.Contracts.Dashboard;
using Fuse.Domain.Identity;
using Fuse.Shared.Enums;

namespace Fuse.Web.Tests;

/// <summary>Card payment links, the payment email and a completed checkout (Stripe, email and webhooks faked).</summary>
public sealed class BookingCheckoutTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(IApi Api, BookingRow Booking)> UnpaidAsync()
    {
        var api = await app.AdminApiAsync();
        var start = TestBookings.BusinessNow.Date.AddDays(9).AddHours(9);
        var (tourId, dateId) = await TestBookings.TourWithDepartureAsync(app, api, start, Ct);
        var reference = await TestBookings.BookAsync(app, tourId, dateId, start, TestBookings.BusinessNow, Ct, paid: false);
        return (api, (await api.Send(new SearchBookings { Keyword = reference.ToString(), PageSize = 100 }, Ct)).Items.Single(r => r.Reference == reference));
    }

    private async Task<BookingRow> RowAsync(IApi api, BookingRow booking) =>
        (await api.Send(new SearchBookings { Keyword = booking.Reference.ToString(), PageSize = 100 }, Ct)).Items.Single(r => r.Id == booking.Id);

    [Fact]
    public async Task A_payment_link_charges_what_the_guest_owes()
    {
        var (api, booking) = await UnpaidAsync();

        var link = await api.Send(new CreatePaymentLink(booking.Id), Ct);

        link.Amount.ShouldBe(50);
        link.QrCodeDataUrl.ShouldStartWith("data:image/png;base64,");
        var session = app.Payments.Sessions[link.Url.Split('/')[^1]];
        session.Request.BookingId.ShouldBe(booking.Id);
        session.Request.CustomerEmail.ShouldBe(TestApp.GuestEmail);
        session.Request.Lines.Sum(l => l.UnitAmount * l.Quantity).ShouldBe(50);
        // As legacy: one line per tour at its total, the guest's existing Stripe customer, and the link recorded on the booking
        // (as its "update" checkout when something is already outstanding).
        var line = session.Request.Lines.ShouldHaveSingleItem();
        (line.UnitAmount, line.Quantity).ShouldBe((50m, 1));
        line.Description!.ShouldStartWith("2 guests at ");
        session.Request.FindExistingCustomer.ShouldBeTrue();
        await using var scope = TestBookings.TenantScope(app);
        (await scope.ServiceProvider.GetRequiredService<Fuse.Infrastructure.Persistence.AppDbContext>().Bookings
            .Where(b => b.Id == booking.Id).Select(b => b.UpdateStripeSessionId).SingleAsync(Ct)).ShouldBe(link.Url.Split('/')[^1]); // it has 50 outstanding
    }

    [Fact]
    public async Task The_payment_email_sends_the_guest_a_link()
    {
        var (api, booking) = await UnpaidAsync();

        (await api.Send(new SendPaymentEmail(booking.Id), Ct)).ShouldContain(TestApp.GuestEmail);

        var email = app.Emails.Sent.Single(e => e.Subject == $"Payment reminder for booking {booking.Reference}");
        email.To.ShouldBe([TestApp.GuestEmail]);
        email.HtmlBody.ShouldContain("Complete Payment");
        email.HtmlBody.ShouldContain("https://checkout.stripe.test/cs_test_");
        // As legacy's reminder: the tour at its per-guest price (the test booking has 2 guests at 50), not its line total.
        email.HtmlBody.ShouldContain(">$50.00</td>");
        email.HtmlBody.ShouldNotContain(">$100.00</td>");
    }

    [Fact]
    public async Task A_completed_checkout_pays_the_booking_once_and_sends_the_confirmation()
    {
        var (api, booking) = await UnpaidAsync();
        await using (var scope = TestBookings.TenantScope(app))
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var guest = await users.FindByEmailAsync(TestApp.GuestEmail);
            guest!.CloudbedsGuestId = "cb-lead";
            await users.UpdateAsync(guest);
        }
        var sessionId = (await api.Send(new CreatePaymentLink(booking.Id), Ct)).Url.Split('/')[^1];
        var anonymous = new Fuse.Web.Client.Api.HttpApi(app.Anonymous());

        (await anonymous.Send(new ConfirmCheckout(sessionId), Ct)).Confirmed.ShouldBeFalse(); // not paid yet
        app.Payments.MarkPaid(sessionId);
        var confirmed = await anonymous.Send(new ConfirmCheckout(sessionId), Ct);
        await anonymous.Send(new ConfirmCheckout(sessionId), Ct); // the webhook arriving too changes nothing

        (confirmed.Confirmed, confirmed.Reference).ShouldBe((true, booking.Reference));
        var row = await RowAsync(api, booking);
        (row.Status, row.Outstanding).ShouldBe((BookingState.Paid, 0m));
        var payment = (await api.Send(new GetBookingOverview(booking.Id), Ct)).Payments.ShouldHaveSingleItem();
        (payment.Amount, payment.Method).ShouldBe((50m, BookingPaymentMethod.Stripe));
        // As legacy's payment-link confirmation (from the booking): the tour at its per-guest price (2 guests at 50).
        var confirmation = app.Emails.Sent.Where(e => ConfirmationOf(e, booking.Reference)).ShouldHaveSingleItem();
        confirmation.HtmlBody.ShouldContain(">$50.00</td>");
        confirmation.HtmlBody.ShouldNotContain(">$100.00</td>");
        app.Webhooks.Sent.Where(w => w.Booking == booking.Id).ShouldHaveSingleItem().CloudbedsIds.ShouldContain("cb-lead");
    }

    [Fact]
    public async Task Paid_bookings_get_no_payment_link_and_the_webhook_is_off_without_a_secret()
    {
        var (api, booking) = await UnpaidAsync();
        await api.Send(new AddBookingPayment(booking.Id, 50, BookingPaymentMethod.Cash, null), Ct);
        (await Should.ThrowAsync<BusinessRuleException>(() => api.Send(new CreatePaymentLink(booking.Id), Ct).AsTask())).Message.ShouldContain("already been paid");

        using var client = app.CreateClient();
        var response = await client.PostAsync("/api/stripe/webhook", new StringContent("{}"), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>The order confirmation for this booking number (its cell in the email, not any number containing it).</summary>
    internal static bool ConfirmationOf(Application.Common.Mail.EmailMessage email, int reference) =>
        email.Subject.EndsWith("Order Confirmation") && email.HtmlBody.Contains($">{reference}</td>");

    private async Task<Fuse.Domain.Catalog.Bookings.BookingPayment[]> PaymentsAsync(DefaultIdType bookingId)
    {
        await using var scope = TestBookings.TenantScope(app);
        return await scope.ServiceProvider.GetRequiredService<Fuse.Infrastructure.Persistence.AppDbContext>().BookingPayments.AsNoTracking()
            .Where(p => p.BookingId == bookingId).OrderBy(p => p.Amount).ToArrayAsync(Ct);
    }

    [Fact]
    public async Task The_return_page_and_the_webhook_confirming_at_once_record_one_payment_and_one_email()
    {
        var (api, booking) = await UnpaidAsync();
        var sessionId = (await api.Send(new CreatePaymentLink(booking.Id), Ct)).Url.Split('/')[^1];
        app.Payments.MarkPaid(sessionId);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            new Fuse.Web.Client.Api.HttpApi(app.Anonymous()).Send(new ConfirmCheckout(sessionId), Ct).AsTask()));

        results.ShouldAllBe(r => r.Confirmed);
        (await PaymentsAsync(booking.Id)).ShouldHaveSingleItem().StripeSessionId.ShouldBe(sessionId);
        app.Emails.Sent.Count(e => ConfirmationOf(e, booking.Reference)).ShouldBe(1);
    }

    [Fact]
    public async Task The_payment_and_confirmation_are_the_bookings_guests_not_the_card_holders()
    {
        var (api, booking) = await UnpaidAsync();
        var sessionId = (await api.Send(new CreatePaymentLink(booking.Id), Ct)).Url.Split('/')[^1];
        app.Payments.MarkPaid(sessionId, paidByEmail: "card-holder@tests.invalid");

        (await new Fuse.Web.Client.Api.HttpApi(app.Anonymous()).Send(new ConfirmCheckout(sessionId), Ct)).Confirmed.ShouldBeTrue();

        var payment = (await PaymentsAsync(booking.Id)).ShouldHaveSingleItem();
        (payment.CustomerName, payment.CustomerEmail).ShouldBe(("Test Guest", TestApp.GuestEmail));
        var confirmation = app.Emails.Sent.Single(e => ConfirmationOf(e, booking.Reference));
        confirmation.To.ShouldBe([TestApp.GuestEmail]);
        confirmation.Bcc.ShouldBeEmpty(); // a payment link, not the website's checkout: the team isn't copied (as legacy)
    }

    [Fact]
    public async Task A_payment_link_recorded_as_a_website_bookings_checkout_does_not_copy_the_team()
    {
        // A staff-made "Website" booking with nothing outstanding recorded yet: the link becomes its checkout, as the
        // website's own would, but it's still a payment link (legacy copied the team only from its website's return page).
        var (api, booking) = await UnpaidAsync();
        await using (var scope = TestBookings.TenantScope(app))
            await scope.ServiceProvider.GetRequiredService<Fuse.Infrastructure.Persistence.AppDbContext>().Database.ExecuteSqlRawAsync(
                "UPDATE Catalog.Bookings SET AmountOutstanding = NULL WHERE Id = {0}", new object[] { booking.Id }, Ct);
        var sessionId = (await api.Send(new CreatePaymentLink(booking.Id), Ct)).Url.Split('/')[^1];
        await using (var scope = TestBookings.TenantScope(app))
            (await scope.ServiceProvider.GetRequiredService<Fuse.Infrastructure.Persistence.AppDbContext>().Bookings
                .Where(b => b.Id == booking.Id).Select(b => new { b.StripeSessionId, b.BookingSource }).SingleAsync(Ct)).ShouldBe(new { StripeSessionId = (string?)sessionId, BookingSource = (string?)"Website" });
        app.Payments.MarkPaid(sessionId);

        (await new Fuse.Web.Client.Api.HttpApi(app.Anonymous()).Send(new ConfirmCheckout(sessionId), Ct)).Confirmed.ShouldBeTrue();

        app.Emails.Sent.Single(e => ConfirmationOf(e, booking.Reference)).Bcc.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_paid_checkout_for_no_booking_is_never_refunded()
    {
        var foreign = await app.Payments.CreateCheckoutAsync(new Application.Common.Payments.CheckoutRequest(Guid.NewGuid(), "Someone else's",
            [new Application.Common.Payments.CheckoutLine("Thing", 20, 1)], "someone@tests.invalid"), Ct);
        app.Payments.MarkPaid(foreign.Id);

        (await new Fuse.Web.Client.Api.HttpApi(app.Anonymous()).Send(new ConfirmCheckout(foreign.Id), Ct)).Confirmed.ShouldBeFalse();

        app.Payments.Refunds.ShouldNotContain(r => r.Session == foreign.Id);
    }

    [Fact]
    public async Task Refunding_a_payment_refunds_only_that_payments_checkout()
    {
        var (api, booking) = await UnpaidAsync();
        var first = (await api.Send(new CreatePaymentLink(booking.Id), Ct)).Url.Split('/')[^1];
        app.Payments.MarkPaid(first);
        await new Fuse.Web.Client.Api.HttpApi(app.Anonymous()).Send(new ConfirmCheckout(first), Ct);
        // A later $10 checkout for a change to the booking (its "update" checkout), and a cash payment.
        var second = await app.Payments.CreateCheckoutAsync(new Application.Common.Payments.CheckoutRequest(booking.Id, "Change",
            [new Application.Common.Payments.CheckoutLine("Change", 10, 1)], TestApp.GuestEmail), Ct);
        app.Payments.MarkPaid(second.Id);
        await using (var scope = TestBookings.TenantScope(app))
            await scope.ServiceProvider.GetRequiredService<Fuse.Infrastructure.Persistence.AppDbContext>().Database.ExecuteSqlRawAsync("""
                INSERT INTO Catalog.BookingPayments (Id, BookingId, Amount, StripeSessionId, Refunded, CreatedBy, CreatedOn, LastModifiedBy, LastModifiedOn,
                    Currency, CustomerEmail, CustomerName, PaymentDate, PartialRefund, PaymentMethod)
                SELECT NEWID(), BookingId, v.Amount, v.Session, 0, CreatedBy, CreatedOn, LastModifiedBy, LastModifiedOn,
                    Currency, CustomerEmail, CustomerName, PaymentDate, NULL, v.Method
                FROM Catalog.BookingPayments CROSS JOIN (VALUES (10, {1}, 0), (5, '', 1)) v (Amount, Session, Method)
                WHERE BookingId = {0}
                """, new object[] { booking.Id, second.Id }, Ct);
        var (cash, change, paid) = (await PaymentsAsync(booking.Id)) is [var c, var u, var p] ? (c, u, p) : throw new InvalidOperationException();

        // The change's payment in full: its own checkout, all it charged; the booking isn't refunded (the first payment isn't).
        (await api.Send(new RefundBookingPayment(change.Id, 10), Ct)).ShouldContain("$10.00");
        app.Payments.Refunds.Where(r => r.Session == second.Id || r.Session == first).ShouldBe([(second.Id, 10m)]);
        var payments = await PaymentsAsync(booking.Id);
        (payments[1].Refunded, payments[2].Refunded).ShouldBe((true, false));
        (await RowAsync(api, booking)).Status.ShouldBe(BookingState.Paid);

        // Part of the first payment: its checkout, that amount; the payment and the booking partly refunded.
        await api.Send(new RefundBookingPayment(paid.Id, 20), Ct);
        app.Payments.Refunds.ShouldContain((first, 20m));
        payments = await PaymentsAsync(booking.Id);
        (payments[2].Refunded, payments[2].PartialRefund).ShouldBe((false, (bool?)true));
        (await RowAsync(api, booking)).Status.ShouldBe(BookingState.PartialRefund);

        // As legacy: a payment not made through Stripe checkout can't be refunded here.
        await Should.ThrowAsync<BusinessRuleException>(() => api.Send(new RefundBookingPayment(cash.Id, 5), Ct).AsTask());
        await Should.ThrowAsync<BusinessRuleException>(() => api.Send(new RefundBookingPayment(change.Id, 10), Ct).AsTask()); // already refunded
    }

    [Fact]
    public async Task Refunding_a_booking_s_only_payment_in_full_refunds_the_booking()
    {
        var (api, booking) = await UnpaidAsync();
        var session = (await api.Send(new CreatePaymentLink(booking.Id), Ct)).Url.Split('/')[^1];
        app.Payments.MarkPaid(session);
        await new Fuse.Web.Client.Api.HttpApi(app.Anonymous()).Send(new ConfirmCheckout(session), Ct);
        var payment = (await PaymentsAsync(booking.Id)).ShouldHaveSingleItem();

        await api.Send(new RefundBookingPayment(payment.Id, payment.Amount), Ct);

        app.Payments.Refunds.ShouldContain((session, 50m));
        (await PaymentsAsync(booking.Id)).ShouldHaveSingleItem().Refunded.ShouldBeTrue();
        (await RowAsync(api, booking)).Status.ShouldBe(BookingState.Refunded);
    }
}
