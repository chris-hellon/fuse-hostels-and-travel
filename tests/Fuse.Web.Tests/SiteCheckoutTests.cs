using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Contracts.Common;
using Fuse.Contracts.Site;
using Fuse.Domain.Identity;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Authorization;

namespace Fuse.Web.Tests;

/// <summary>The website's basket and checkout: server pricing, who the booking belongs to, places and payment.</summary>
public sealed class SiteCheckoutTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private Fuse.Web.Client.Api.HttpApi Anonymous => new(app.Anonymous());

    /// <summary>A published tour with one departure (25 a head, 10 places) picking up at the test hostel.</summary>
    private async Task<(DefaultIdType TourId, DefaultIdType DepartureId)> DepartureAsync(int places = 10)
    {
        var api = await app.AdminApiAsync();
        var (tourId, departureId) = await TestBookings.TourWithDepartureAsync(app, api, TestBookings.BusinessNow.Date.AddDays(4).AddHours(9), Ct);
        await using var scope = TestBookings.TenantScope(app);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("UPDATE Catalog.Tours SET PublishToSite = 1 WHERE Id = {0}", new object[] { tourId }, Ct);
        await db.Database.ExecuteSqlRawAsync("UPDATE Catalog.TourDates SET AvailableSpaces = {0} WHERE Id = {1}", new object[] { places, departureId }, Ct);
        return (tourId, departureId);
    }

    private async Task<int> PlacesAsync(DefaultIdType departureId)
    {
        await using var scope = TestBookings.TenantScope(app);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().TourDates.Where(d => d.Id == departureId).Select(d => d.AvailableSpaces).SingleAsync(Ct);
    }

    private static CheckoutContact Contact(string email) => new()
    {
        FirstName = "Sam", LastName = "Walker", Email = email, PhoneNumber = "+84 1", DateOfBirth = new DateTime(1995, 5, 5), Gender = "Female", Nationality = "Australia",
    };

    [Fact]
    public async Task The_basket_is_priced_on_the_server_and_problems_are_named()
    {
        var (_, departureId) = await DepartureAsync(places: 2);

        var quote = await Anonymous.Send(new QuoteBasket([new BasketLineRequest(departureId, 2, null), new BasketLineRequest(Guid.NewGuid(), 1, null)]), Ct);

        quote.Lines[0].UnitPrice.ShouldBe(25m);
        quote.Lines[0].Problem.ShouldBeNull();
        quote.Lines[0].Pickup.ShouldBe(TestApp.SeedPropertyName); // the only pick-up point
        quote.Lines[1].Problem.ShouldBe("This departure is no longer available.");
        quote.Bookable.ShouldBeFalse();
        (await Anonymous.Send(new QuoteBasket([new BasketLineRequest(departureId, 3, null)]), Ct)).Lines[0].Problem.ShouldBe("Only 2 places left.");
    }

    [Fact]
    public async Task A_new_visitor_books_as_a_new_guest_without_a_password_and_takes_the_places()
    {
        var (_, departureId) = await DepartureAsync();
        var email = $"new-{Guid.NewGuid():N}@tests.invalid";
        var friend = new CheckoutGuest { FirstName = "Alex", LastName = "Friend" };

        var placed = await Anonymous.Send(new PlaceBooking(Contact(email), [friend],
            [new CheckoutTour { DepartureId = departureId, GuestKeys = [friend.Key] }], null, null, "Veggie please"), Ct);

        placed.PaymentUrl.ShouldStartWith("https://checkout.stripe.test/");
        (await PlacesAsync(departureId)).ShouldBe(8);
        await using var scope = TestBookings.TenantScope(app);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var booking = await db.Bookings.Include(b => b.Items).ThenInclude(i => i.Guests).SingleAsync(b => b.Id == placed.BookingId, Ct);
        booking.BookingSource.ShouldBe("Website");
        booking.TotalAmount.ShouldBe(50m);
        booking.IsPaid.ShouldBeFalse();
        booking.AdditionalNotes.ShouldBe("Veggie please");
        booking.Items.Single().Guests!.Count.ShouldBe(1);
        // As legacy: nothing outstanding until a payment is taken (so it reads Unpaid, not Partially Paid), and no waiver yet.
        booking.AmountOutstanding.ShouldBeNull();
        booking.WaiverSigned.ShouldBe(false);
        booking.Items.Single().WaiverSigned.ShouldBe(false);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var guest = (await users.FindByEmailAsync(email))!;
        (await users.HasPasswordAsync(guest)).ShouldBeFalse();
        (await users.IsInRoleAsync(guest, FuseRoles.Guest)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_known_email_gets_the_booking_but_its_account_is_left_alone()
    {
        var (_, departureId) = await DepartureAsync();
        string before;
        await using (var scope = TestBookings.TenantScope(app))
            before = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(TestApp.GuestEmail))!.FirstName!;

        var placed = await Anonymous.Send(new PlaceBooking(Contact(TestApp.GuestEmail), [], [new CheckoutTour { DepartureId = departureId }], "hijack", "hijack", null), Ct);

        await using var after = TestBookings.TenantScope(app);
        var users = after.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var guest = (await users.FindByEmailAsync(TestApp.GuestEmail))!;
        guest.FirstName.ShouldBe(before); // not overwritten with "Sam"
        (await users.CheckPasswordAsync(guest, "hijack")).ShouldBeFalse();
        var booking = await after.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.SingleAsync(b => b.Id == placed.BookingId, Ct);
        booking.GuestId.ShouldBe(guest.Id);
    }

    [Fact]
    public async Task Too_few_places_is_refused_and_nothing_is_booked()
    {
        var (_, departureId) = await DepartureAsync(places: 1);
        var friend = new CheckoutGuest { FirstName = "Alex", LastName = "Friend" };

        await Should.ThrowAsync<BusinessRuleException>(() => Anonymous.Send(new PlaceBooking(Contact($"x-{Guid.NewGuid():N}@tests.invalid"), [friend],
            [new CheckoutTour { DepartureId = departureId, GuestKeys = [friend.Key] }], null, null, null), Ct).AsTask());

        (await PlacesAsync(departureId)).ShouldBe(1);
    }

    [Fact]
    public async Task Checkout_needs_the_bookers_details()
    {
        var (_, departureId) = await DepartureAsync();

        await Should.ThrowAsync<RequestValidationException>(() => Anonymous.Send(new PlaceBooking(new CheckoutContact(), [],
            [new CheckoutTour { DepartureId = departureId }], null, null, null), Ct).AsTask());
    }

    [Fact]
    public async Task Checking_out_again_replaces_the_unpaid_attempt_and_gives_its_places_back()
    {
        var (_, departureId) = await DepartureAsync(places: 5);
        var email = $"again-{Guid.NewGuid():N}@tests.invalid";
        var first = await Anonymous.Send(new PlaceBooking(Contact(email), [], [new CheckoutTour { DepartureId = departureId }], null, null, null), Ct);
        (await PlacesAsync(departureId)).ShouldBe(4);

        // Someone else's id (a different email) is ignored.
        await Anonymous.Send(new PlaceBooking(Contact($"other-{Guid.NewGuid():N}@tests.invalid"), [], [new CheckoutTour { DepartureId = departureId }], null, null, null, first.BookingId), Ct);
        (await PlacesAsync(departureId)).ShouldBe(3);

        var second = await Anonymous.Send(new PlaceBooking(Contact(email), [], [new CheckoutTour { DepartureId = departureId }], null, null, null, first.BookingId), Ct);

        (await PlacesAsync(departureId)).ShouldBe(3); // the first attempt's place came back, the second took one
        await using var scope = TestBookings.TenantScope(app);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Bookings.AnyAsync(b => b.Id == first.BookingId, Ct)).ShouldBeFalse();
        (await db.Bookings.AnyAsync(b => b.Id == second.BookingId, Ct)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("/basket", "Basket")]
    [InlineData("/checkout", "Check Out")]
    public async Task The_basket_and_checkout_pages_load(string path, string heading)
    {
        var response = await app.Anonymous().GetAsync(path, Ct);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain(heading);
    }

    [Fact]
    public async Task An_unpaid_website_checkout_is_payable_for_stripes_default_and_stays_unpaid_for_staff_as_legacy()
    {
        var (_, departureId) = await DepartureAsync(places: 5);
        var placed = await Anonymous.Send(new PlaceBooking(Contact($"expire-{Guid.NewGuid():N}@tests.invalid"), [], [new CheckoutTour { DepartureId = departureId }], null, null, null), Ct);
        var session = app.Payments.Sessions.Single(s => s.Value.Request.BookingId == placed.BookingId);
        session.Value.Request.WebsiteCheckout.ShouldBeTrue(); // as legacy: no expiry set, so Stripe's default 24 hours
        (await PlacesAsync(departureId)).ShouldBe(4);
        await using var scope = TestBookings.TenantScope(app);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(
            "UPDATE Catalog.Bookings SET BookingDate = DATEADD(hour, -3, BookingDate) WHERE Id = {0}", new object[] { placed.BookingId }, Ct);

        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.AnyAsync(b => b.Id == placed.BookingId && !b.IsPaid, Ct)).ShouldBeTrue();
        (await PlacesAsync(departureId)).ShouldBe(4);
    }

    [Fact]
    public async Task A_paid_website_checkout_confirms_to_the_guest_and_copies_the_team()
    {
        var (_, departureId) = await DepartureAsync();
        var email = $"paid-{Guid.NewGuid():N}@tests.invalid";
        var friend = new CheckoutGuest { FirstName = "Alex", LastName = "Friend" };
        var placed = await Anonymous.Send(new PlaceBooking(Contact(email), [friend],
            [new CheckoutTour { DepartureId = departureId, GuestKeys = [friend.Key] }], null, null, null), Ct);
        var session = app.Payments.Sessions.Single(s => s.Value.Request.BookingId == placed.BookingId).Key;
        app.Payments.MarkPaid(session, paidByEmail: "card-holder@tests.invalid");

        var confirmed = await Anonymous.Send(new Fuse.Contracts.Bookings.ConfirmCheckout(session), Ct);

        confirmed.Confirmed.ShouldBeTrue();
        var confirmation = app.Emails.Sent.Single(e => BookingCheckoutTests.ConfirmationOf(e, confirmed.Reference!.Value));
        confirmation.To.ShouldBe([email]);
        confirmation.Bcc.ShouldContain("team@tests.invalid"); // as legacy: MailSettings:ToAddress (Site:ContactEmail)
        // As legacy's website confirmation (from its basket): the line at its total, 2 guests at 25.
        confirmation.HtmlBody.ShouldContain(">$50.00</td>");
        confirmation.HtmlBody.ShouldNotContain(">$25.00</td>");
        await using var scope = TestBookings.TenantScope(app);
        var booking = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.Include(b => b.Payments).SingleAsync(b => b.Id == placed.BookingId, Ct);
        (booking.IsPaid, booking.AmountOutstanding).ShouldBe((true, (decimal?)0m));
        booking.Payments.ShouldHaveSingleItem().CustomerEmail.ShouldBe(email);
    }

    [Fact]
    public async Task Legacys_website_return_address_still_confirms_the_payment()
    {
        var response = await app.Anonymous().GetAsync("/payment-confirmation/cs_test_unknown", Ct);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Order");
    }
}
