using Shouldly;
using Fuse.Contracts.Api;
using Fuse.Contracts.Calendar;
using Fuse.Contracts.Dashboard;
using Fuse.Contracts.Identity;

namespace Fuse.Web.Tests;

public sealed class DashboardTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTime BusinessNow => TestBookings.BusinessNow;
    private Task<(DefaultIdType TourId, DefaultIdType TourDateId)> TourWithDepartureAsync(IApi api, DateTime start, string? supplierId = null) =>
        TestBookings.TourWithDepartureAsync(app, api, start, Ct, supplierId);
    private Task<int> BookAsync(DefaultIdType tourId, DefaultIdType tourDateId, DateTime start, DateTime bookedAt, bool paid = true) =>
        TestBookings.BookAsync(app, tourId, tourDateId, start, bookedAt, Ct, paid);

    [Fact]
    public async Task Departures_the_calendar_and_its_guest_list_show_a_booked_departure()
    {
        var api = await app.AdminApiAsync();
        var start = BusinessNow.Date.AddDays(1).AddHours(9); // tomorrow, business time
        var (tourId, dateId) = await TourWithDepartureAsync(api, start);
        var reference = await BookAsync(tourId, dateId, start, BusinessNow);

        var departures = await api.Send(new SearchDepartures { DaysFromToday = 1, TourId = tourId, PageSize = 50 }, Ct);
        departures.Items.Count.ShouldBe(2); // lead guest + the additional guest
        departures.Items.ShouldAllBe(r => r.Reference == reference && r.Status == BookingState.Paid && r.StartDate == start);

        var calendar = await api.Send(new ListCalendarDepartures(start.Date.AddDays(-3), start.Date.AddDays(3)), Ct);
        calendar.Where(d => d.TourId == tourId).ShouldHaveSingleItem().Guests.ShouldBe(2);

        var guests = await api.Send(new ListDepartureGuests(tourId, start), Ct);
        guests.Count.ShouldBe(2);
        guests.ShouldAllBe(g => g.Reference == reference && g.WaiverSigned);
        guests.Select(g => g.BookingId).Distinct().ShouldHaveSingleItem().ShouldNotBeNull(); // the reference opens its booking

        var file = await api.Send(new ExportDepartureGuests(tourId, start), Ct);
        file.FileName.ShouldEndWith(".xlsx");
        file.Content.Take(2).ShouldBe("PK"u8.ToArray()); // a zip (xlsx) file
        (await api.Send(new ExportDepartures(1, tourId, null), Ct)).Content.Length.ShouldBeGreaterThan(1000);
    }

    [Fact]
    public async Task Totals_count_bookings_by_booking_date_and_today_shows_todays_bookings()
    {
        var api = await app.AdminApiAsync();
        var start = DateTime.Today.AddDays(30).AddHours(10);
        var (tourId, dateId) = await TourWithDepartureAsync(api, start);
        var day = new DateTime(2001, 1, 1).AddDays(Random.Shared.Next(3000));
        await BookAsync(tourId, dateId, start, day.AddHours(12));
        var today = await BookAsync(tourId, dateId, start, BusinessNow);

        var totals = await api.Send(new GetDashboardStats(day, day), Ct);
        totals.TotalBookings.ShouldBe(1);
        totals.TotalRevenue.ShouldBe(50); // price $25 × 2 guests
        totals.TodaysBookings.ShouldBeGreaterThanOrEqualTo(1);

        var activity = await api.Send(new SearchTodaysBookings { TourId = tourId }, Ct);
        activity.Items.ShouldHaveSingleItem().Reference.ShouldBe(today);
    }

    [Fact]
    public async Task Suppliers_only_see_their_own_tours_and_none_without_tours()
    {
        var admin = await app.AdminApiAsync();
        var start = DateTime.Today.AddDays(500 + Random.Shared.Next(200)).AddHours(8);

        var supplierEmail = $"supplier-{Guid.NewGuid():N}@tests.invalid";
        var supplierId = await admin.Send(new CreateSupplier(new UserProfile { FirstName = "Sam", LastName = "Supplies", Email = supplierEmail }, TestApp.Password, TestApp.Password), Ct);
        var idleEmail = $"supplier-{Guid.NewGuid():N}@tests.invalid";
        await admin.Send(new CreateSupplier(new UserProfile { FirstName = "Ida", LastName = "Idle", Email = idleEmail }, TestApp.Password, TestApp.Password), Ct);

        var (theirs, theirDate) = await TourWithDepartureAsync(admin, start, supplierId);
        var (other, otherDate) = await TourWithDepartureAsync(admin, start);
        await BookAsync(theirs, theirDate, start, BusinessNow);
        await BookAsync(other, otherDate, start, BusinessNow);

        var supplier = new Fuse.Web.Client.Api.HttpApi(await app.SignedInAsync(supplierEmail));
        var calendar = await supplier.Send(new ListCalendarDepartures(start.Date, start.Date), Ct);
        calendar.Select(d => d.TourId).ShouldBe([theirs]);
        (await supplier.Send(new ListDepartureGuests(other, start), Ct)).ShouldBeEmpty();

        var idle = new Fuse.Web.Client.Api.HttpApi(await app.SignedInAsync(idleEmail));
        (await idle.Send(new ListCalendarDepartures(start.Date, start.Date), Ct)).ShouldBeEmpty();
    }
}
