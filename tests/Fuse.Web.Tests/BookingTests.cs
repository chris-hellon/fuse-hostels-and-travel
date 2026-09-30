using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Contracts.Api;
using Fuse.Contracts.Bookings;
using Fuse.Contracts.Common;
using Fuse.Contracts.Dashboard;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Enums;

namespace Fuse.Web.Tests;

public sealed class BookingTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A departure in a week with a booking on it for two guests (lead + one), at $50 each.</summary>
    private async Task<(IApi Api, BookingRow Booking, DefaultIdType TourId, DefaultIdType TourDateId)> BookedAsync(bool paid)
    {
        var api = await app.AdminApiAsync();
        var start = TestBookings.BusinessNow.Date.AddDays(7).AddHours(9);
        var (tourId, dateId) = await TestBookings.TourWithDepartureAsync(app, api, start, Ct);
        var reference = await TestBookings.BookAsync(app, tourId, dateId, start, TestBookings.BusinessNow, Ct, paid);
        // The keyword also matches totals (legacy), so pick the booking by its reference.
        var row = (await api.Send(new SearchBookings { Keyword = reference.ToString(), PageSize = 100 }, Ct)).Items.Single(r => r.Reference == reference);
        return (api, row, tourId, dateId);
    }

    private async Task<int> SpacesAsync(DefaultIdType tourDateId)
    {
        await using var scope = TestBookings.TenantScope(app);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().TourDates.Where(d => d.Id == tourDateId).Select(d => d.AvailableSpaces).SingleAsync(Ct);
    }

    [Fact]
    public async Task Bookings_are_found_by_reference_tour_and_departure_dates()
    {
        var (api, row, tourId, _) = await BookedAsync(paid: true);
        row.Status.ShouldBe(BookingState.Paid);
        row.Outstanding.ShouldBe(0);
        row.GuestHasEmail.ShouldBeTrue();

        var byTour = await api.Send(new SearchBookings { TourId = tourId, PageSize = 50 }, Ct);
        byTour.Items.ShouldHaveSingleItem().Id.ShouldBe(row.Id);

        var inWeek = await api.Send(new SearchBookings { TourId = tourId, DepartsFrom = DateTime.Today.AddDays(6), DepartsTo = DateTime.Today.AddDays(8) }, Ct);
        inWeek.Items.ShouldHaveSingleItem();
        var tomorrow = await api.Send(new SearchBookings { TourId = tourId, DepartsFrom = DateTime.Today, DepartsTo = DateTime.Today.AddDays(1) }, Ct);
        tomorrow.Items.ShouldBeEmpty();

        (await api.Send(new SearchBookings { TourId = tourId, Source = "Website" }, Ct)).Items.ShouldHaveSingleItem();
        (await api.Send(new SearchBookings { TourId = tourId, Source = "Online" }, Ct)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_rows_details_list_the_tours_guests_and_payments()
    {
        var (api, row, _, _) = await BookedAsync(paid: true);

        var overview = await api.Send(new GetBookingOverview(row.Id), Ct);
        var item = overview.Items.ShouldHaveSingleItem();
        item.GuestCount.ShouldBe(2);
        item.Price.ShouldBe(50);
        item.Total.ShouldBe(100);
        item.Guests.Count.ShouldBe(2);
        item.Guests[0].GuestId.ShouldBe(overview.GuestId);
        item.Status.ShouldBe(BookingState.Paid);
        overview.BookingSource.ShouldBe("Website");
    }

    [Fact]
    public async Task Payments_reduce_the_balance_until_the_booking_is_paid()
    {
        var (api, row, _, _) = await BookedAsync(paid: false);
        row.Status.ShouldBe(BookingState.PartiallyPaid); // the test booking starts with its $50 outstanding
        row.Outstanding.ShouldBe(50);

        await Should.ThrowAsync<BusinessRuleException>(() => api.Send(new AddBookingPayment(row.Id, 60, BookingPaymentMethod.Cash, null), Ct).AsTask());
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new AddBookingPayment(row.Id, 10, BookingPaymentMethod.Stripe, null), Ct).AsTask());

        await api.Send(new AddBookingPayment(row.Id, 20, BookingPaymentMethod.Cash, null), Ct);
        var part = (await api.Send(new SearchBookings { Keyword = row.Reference.ToString(), PageSize = 100 }, Ct)).Items.Single(r => r.Id == row.Id);
        (part.Status, part.Outstanding).ShouldBe((BookingState.PartiallyPaid, 30m));

        await api.Send(new AddBookingPayment(row.Id, 30, BookingPaymentMethod.Stripe, "pi_test_123"), Ct);
        var paid = (await api.Send(new SearchBookings { Keyword = row.Reference.ToString(), PageSize = 100 }, Ct)).Items.Single(r => r.Id == row.Id);
        (paid.Status, paid.Outstanding, paid.IsPaid).ShouldBe((BookingState.Paid, 0m, true));

        var payments = (await api.Send(new GetBookingOverview(row.Id), Ct)).Payments;
        payments.Select(p => (p.Amount, p.Method)).ShouldBe([(20m, BookingPaymentMethod.Cash), (30m, BookingPaymentMethod.Stripe)]);
    }

    [Fact]
    public async Task Deleting_an_unpaid_booking_frees_its_spaces_but_paid_ones_cant_be_deleted()
    {
        var (api, unpaid, _, dateId) = await BookedAsync(paid: false);
        var before = await SpacesAsync(dateId);
        await api.Send(new DeleteBooking(unpaid.Id), Ct);
        (await SpacesAsync(dateId)).ShouldBe(before + 2);
        (await api.Send(new SearchBookings { Keyword = unpaid.Reference.ToString(), PageSize = 100 }, Ct)).Items.ShouldNotContain(r => r.Id == unpaid.Id);

        var (_, paid, _, _) = await BookedAsync(paid: true);
        await Should.ThrowAsync<BusinessRuleException>(() => api.Send(new DeleteBooking(paid.Id), Ct).AsTask());
    }

    [Fact]
    public async Task Cancelling_frees_the_spaces_and_a_cash_refund_is_recorded()
    {
        var (api, row, _, dateId) = await BookedAsync(paid: true);
        var before = await SpacesAsync(dateId);

        var message = await api.Send(new CancelBooking(row.Id, 10), Ct);
        message.ShouldContain("$10.00 recorded as refunded");
        (await SpacesAsync(dateId)).ShouldBe(before + 2);

        var cancelled = (await api.Send(new SearchBookings { Keyword = row.Reference.ToString(), PageSize = 100 }, Ct)).Items.Single(r => r.Id == row.Id);
        cancelled.Status.ShouldBe(BookingState.Cancelled);
        (await api.Send(new GetBookingOverview(row.Id), Ct)).Items.Single().Guests.ShouldAllBe(g => g.Status == BookingState.Cancelled);
        await Should.ThrowAsync<BusinessRuleException>(() => api.Send(new CancelBooking(row.Id, 0), Ct).AsTask());
    }

    [Fact]
    public async Task Refunds_are_partial_below_the_total_and_card_refunds_go_through_stripe()
    {
        var (api, row, _, _) = await BookedAsync(paid: true);
        await Should.ThrowAsync<BusinessRuleException>(() => api.Send(new RefundBooking(row.Id, 500), Ct).AsTask());
        await api.Send(new RefundBooking(row.Id, 20), Ct);
        (await api.Send(new SearchBookings { Keyword = row.Reference.ToString(), PageSize = 100 }, Ct)).Items.Single(r => r.Id == row.Id).Status.ShouldBe(BookingState.PartialRefund);

        // Paid through Stripe checkout: a full refund returns what the checkout took.
        var (_, card, _, _) = await BookedAsync(paid: true);
        var session = (await app.Payments.CreateCheckoutAsync(new(card.Id, "Test", [new("Tour", 100, 1)], null), Ct)).Id;
        app.Payments.MarkPaid(session);
        await using (var scope = TestBookings.TenantScope(app))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Bookings.SingleAsync(b => b.Id == card.Id, Ct)).SetStripeSessonId(session);
            await db.SaveChangesAsync(Ct);
        }
        (await api.Send(new RefundBooking(card.Id, 50), Ct)).ShouldContain("$100.00 refunded through Stripe");
        app.Payments.Refunds.ShouldContain((session, 100m));
        (await api.Send(new SearchBookings { Keyword = card.Reference.ToString(), PageSize = 100 }, Ct)).Items.Single(r => r.Id == card.Id).Status.ShouldBe(BookingState.Refunded);
    }

    [Fact]
    public async Task Cancelling_the_only_tour_cancels_the_booking()
    {
        var (api, row, _, dateId) = await BookedAsync(paid: true);
        var before = await SpacesAsync(dateId);
        var item = (await api.Send(new GetBookingOverview(row.Id), Ct)).Items.Single();

        await api.Send(new CancelBookingItem(item.Id, 0), Ct);

        (await SpacesAsync(dateId)).ShouldBe(before + 2);
        (await api.Send(new SearchBookings { Keyword = row.Reference.ToString(), PageSize = 100 }, Ct)).Items.Single(r => r.Id == row.Id).Status.ShouldBe(BookingState.Cancelled);
    }

    [Fact]
    public async Task Only_configured_staff_can_flag_bookings_paid()
    {
        var (api, row, _, _) = await BookedAsync(paid: false);
        (await api.Send(new GetBookingAbilities(), Ct)).CanFlagAsPaid.ShouldBeFalse();
        await Should.ThrowAsync<ForbiddenException>(() => api.Send(new FlagBookingAsPaid(row.Id), Ct).AsTask());
    }

    [Fact]
    public async Task Export_caps_each_range_at_a_month()
    {
        var today = new DateTime(2026, 9, 24);
        var none = ExportWindow.Normalize(new ExportBookings(), today);
        (none.Export.BookedFrom, none.Export.BookedTo).ShouldBe((today.AddMonths(-1), today));
        none.Notes.ShouldHaveSingleItem();

        var wide = ExportWindow.Normalize(new ExportBookings { DepartsFrom = new DateTime(2026, 1, 1), DepartsTo = new DateTime(2026, 6, 1) }, today);
        (wide.Export.DepartsFrom, wide.Export.DepartsTo).ShouldBe((new DateTime(2026, 1, 1), new DateTime(2026, 1, 31)));
        wide.Export.BookedFrom.ShouldBeNull();

        var (api, row, tourId, _) = await BookedAsync(paid: true);
        var file = await api.Send(new ExportBookings { TourId = tourId, BookedFrom = DateTime.Today.AddDays(-1), BookedTo = DateTime.Today.AddDays(1) }, Ct);
        file.ContentType.ShouldBe(FileDownload.Xlsx);
        file.Content.Length.ShouldBeGreaterThan(0);
    }
}
