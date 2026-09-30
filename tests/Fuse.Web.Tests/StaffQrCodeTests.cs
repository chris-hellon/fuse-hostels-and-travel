using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Contracts.Common;
using Fuse.Contracts.Identity;
using Fuse.Contracts.Site;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Text;

namespace Fuse.Web.Tests;

/// <summary>Staff booking QR codes (legacy's staff booking link): tours shown only by QR code, at prices not published to the
/// website (never in-house ones), and the booking credited to the staff member.</summary>
public sealed class StaffQrCodeTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private Fuse.Web.Client.Api.HttpApi Anonymous => new(app.Anonymous());

    /// <summary>A tour not on the website, shown on QR codes, whose only price isn't published to the website.</summary>
    private async Task<(DefaultIdType TourId, DefaultIdType DepartureId, string Slug)> QrOnlyTourAsync()
    {
        var api = await app.AdminApiAsync();
        var (tourId, departureId) = await TestBookings.TourWithDepartureAsync(app, api, TestBookings.BusinessNow.Date.AddDays(3).AddHours(9), Ct);
        await using var scope = TestBookings.TenantScope(app);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("UPDATE Catalog.Tours SET PublishToSite = 0, ShowBookingQRCode = 1 WHERE Id = {0}", new object[] { tourId }, Ct);
        await db.Database.ExecuteSqlRawAsync("UPDATE Catalog.TourPrices SET PublishToWebsite = 0 WHERE TourId = {0}", new object[] { tourId }, Ct);
        var name = await db.Tours.Where(t => t.Id == tourId).Select(t => t.Name).SingleAsync(Ct);
        return (tourId, departureId, Slugs.From(name));
    }

    private async Task<DefaultIdType> UserIdAsync(string email)
    {
        await using var scope = TestBookings.TenantScope(app);
        return DefaultIdType.Parse(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync(Ct));
    }

    private async Task<DefaultIdType> BookingCreatedByAsync(DefaultIdType bookingId)
    {
        await using var scope = TestBookings.TenantScope(app);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.IgnoreQueryFilters().Where(b => b.Id == bookingId).Select(b => b.CreatedBy).SingleAsync(Ct);
    }

    private async Task<(string Email, DefaultIdType Id)> SupplierAsync()
    {
        var api = await app.AdminApiAsync();
        var email = $"supplier-{Guid.NewGuid():N}@tests.invalid";
        var id = await api.Send(new CreateSupplier(new UserProfile { FirstName = "Sam", LastName = "Supplier", Email = email }, TestApp.Password, TestApp.Password), Ct);
        return (email, DefaultIdType.Parse(id));
    }

    private static CheckoutContact Contact() => new()
    {
        FirstName = "Sam", LastName = "Walker", Email = $"qr-{Guid.NewGuid():N}@tests.invalid", PhoneNumber = "+84 1",
        DateOfBirth = new DateTime(1995, 5, 5), Gender = "Female", Nationality = "Australia",
    };

    [Fact]
    public async Task A_staff_link_shows_a_qr_only_tour_at_unpublished_prices_and_credits_the_booking_to_them()
    {
        var (tourId, departureId, slug) = await QrOnlyTourAsync();
        var adminId = await UserIdAsync(TestApp.AdminEmail);

        // Not on the website.
        await Should.ThrowAsync<NotFoundException>(() => Anonymous.Send(new GetSiteTour(slug), Ct).AsTask());

        var tour = await Anonymous.Send(new GetSiteTour(slug, adminId), Ct);
        tour.Id.ShouldBe(tourId);
        tour.StaffName.ShouldBe("Admin");
        tour.Prices.Select(p => p.Price).ShouldBe([25m]);
        (await Anonymous.Send(new ListSiteTourDepartures(tourId, adminId), Ct)).ShouldHaveSingleItem().Id.ShouldBe(departureId);
        (await Anonymous.Send(new QuoteBasket([new BasketLineRequest(departureId, 1, null)], adminId), Ct)).Lines[0].Problem.ShouldBeNull();

        var placed = await Anonymous.Send(new PlaceBooking(Contact(), [], [new CheckoutTour { DepartureId = departureId }], null, null, null, null, adminId), Ct);
        (await BookingCreatedByAsync(placed.BookingId)).ShouldBe(adminId);

        var response = await app.Anonymous().GetAsync($"/tours/staff-booking/{slug}/{adminId}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync(Ct);
        html.ShouldContain("Booking with Admin");
        html.ShouldContain("noindex");
    }

    [Fact]
    public async Task A_staff_link_never_sells_in_house_prices()
    {
        var (tourId, departureId, slug) = await QrOnlyTourAsync();
        var adminId = await UserIdAsync(TestApp.AdminEmail);
        await using (var scope = TestBookings.TenantScope(app))
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
                .ExecuteSqlRawAsync("UPDATE Catalog.TourPrices SET InHouseOnly = 1 WHERE TourId = {0}", new object[] { tourId }, Ct);

        // As legacy's staff booking page: in-house prices, and their departures, are removed whoever's link it is.
        (await Anonymous.Send(new GetSiteTour(slug, adminId), Ct)).Prices.ShouldBeEmpty();
        (await Anonymous.Send(new ListSiteTourDepartures(tourId, adminId), Ct)).ShouldBeEmpty();
        (await Anonymous.Send(new QuoteBasket([new BasketLineRequest(departureId, 1, null)], adminId), Ct)).Lines[0].Problem.ShouldNotBeNull();
        await Should.ThrowAsync<BusinessRuleException>(() =>
            Anonymous.Send(new PlaceBooking(Contact(), [], [new CheckoutTour { DepartureId = departureId }], null, null, null, null, adminId), Ct).AsTask());
    }

    [Fact]
    public async Task An_unknown_or_supplier_staff_id_is_just_the_public_website()
    {
        var (_, departureId, slug) = await QrOnlyTourAsync();
        var (_, supplierId) = await SupplierAsync();

        foreach (var staffId in new[] { supplierId, DefaultIdType.NewGuid() })
        {
            await Should.ThrowAsync<NotFoundException>(() => Anonymous.Send(new GetSiteTour(slug, staffId), Ct).AsTask());
            (await Anonymous.Send(new QuoteBasket([new BasketLineRequest(departureId, 1, null)], staffId), Ct)).Lines[0].Problem.ShouldNotBeNull();
        }

        // A public tour booked through a supplier's id is the guest's own booking.
        var api = await app.AdminApiAsync();
        var (tourId, publicDeparture) = await TestBookings.TourWithDepartureAsync(app, api, TestBookings.BusinessNow.Date.AddDays(3).AddHours(9), Ct);
        await using (var scope = TestBookings.TenantScope(app))
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
                .ExecuteSqlRawAsync("UPDATE Catalog.Tours SET PublishToSite = 1 WHERE Id = {0}", new object[] { tourId }, Ct);
        var contact = Contact();
        var placed = await Anonymous.Send(new PlaceBooking(contact, [], [new CheckoutTour { DepartureId = publicDeparture }], null, null, null, null, supplierId), Ct);
        (await BookingCreatedByAsync(placed.BookingId)).ShouldBe(await UserIdAsync(contact.Email));
    }

    [Fact]
    public async Task Staff_get_a_qr_code_per_tour_and_suppliers_get_none()
    {
        var (_, _, slug) = await QrOnlyTourAsync();
        var adminId = await UserIdAsync(TestApp.AdminEmail);

        var codes = await (await app.AdminApiAsync()).Send(new ListMyTourQrCodes(), Ct);
        codes.ShouldContain(c => c.Url.EndsWith($"/tours/staff-booking/{slug}/{adminId}"));
        codes.Single(c => c.Url.EndsWith($"/tours/staff-booking/{slug}/{adminId}")).PngDataUrl.ShouldStartWith("data:image/png;base64,");

        var (supplierEmail, _) = await SupplierAsync();
        var supplier = new Fuse.Web.Client.Api.HttpApi(await app.SignedInAsync(supplierEmail, staffOnly: true));
        (await supplier.Send(new ListMyTourQrCodes(), Ct)).ShouldBeEmpty();
    }
}
