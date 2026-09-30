using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Contracts.Common;
using Fuse.Contracts.Site;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Text;

namespace Fuse.Web.Tests;

/// <summary>The public tours: a tour's page and the departures visitors can book.</summary>
public sealed class SiteTourTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(DefaultIdType TourId, DefaultIdType DepartureId, string Slug)> PublishedTourAsync()
    {
        var api = await app.AdminApiAsync();
        var start = TestBookings.BusinessNow.Date.AddDays(3).AddHours(9);
        var (tourId, departureId) = await TestBookings.TourWithDepartureAsync(app, api, start, Ct);
        await using var scope = TestBookings.TenantScope(app);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("UPDATE Catalog.Tours SET PublishToSite = 1 WHERE Id = {0}", new object[] { tourId }, Ct);
        var name = await db.Tours.Where(t => t.Id == tourId).Select(t => t.Name).SingleAsync(Ct);
        return (tourId, departureId, Slugs.From(name));
    }

    [Fact]
    public async Task A_published_tour_has_a_page_with_its_web_prices_pickups_and_departures()
    {
        var (tourId, departureId, slug) = await PublishedTourAsync();
        var anonymous = new Fuse.Web.Client.Api.HttpApi(app.Anonymous());

        var tour = await anonymous.Send(new GetSiteTour(slug), Ct);
        tour.Id.ShouldBe(tourId);
        tour.Duration.ShouldBe("2 Hours");
        tour.Prices.Select(p => p.Price).ShouldBe([25m]);
        tour.Pickups.ShouldBe([TestApp.SeedPropertyName]);

        var departures = await anonymous.Send(new ListSiteTourDepartures(tourId), Ct);
        departures.Single().Id.ShouldBe(departureId);
        departures.Single().Price.ShouldBe(25m);

        var response = await app.Anonymous().GetAsync($"/tours/{slug}", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("BOOK YOUR TOUR");
        (await app.Anonymous().GetAsync("/tours/no-such-tour", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Departures_at_prices_not_for_the_website_are_not_offered()
    {
        var (tourId, _, _) = await PublishedTourAsync();
        await using (var scope = TestBookings.TenantScope(app))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("UPDATE Catalog.TourPrices SET InHouseOnly = 1 WHERE TourId = {0}", new object[] { tourId }, Ct);
        }
        var anonymous = new Fuse.Web.Client.Api.HttpApi(app.Anonymous());

        (await anonymous.Send(new ListSiteTourDepartures(tourId), Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_tours_page_lists_published_tours()
    {
        var (_, _, slug) = await PublishedTourAsync();

        var html = await app.Anonymous().GetStringAsync("/tours", Ct);

        html.ShouldContain("<title>Tours - Fuse Hostels &amp; Travel</title>");
        html.ShouldContain($"href=\"/tours/{slug}\"");
    }

    [Fact]
    public async Task Legacy_tour_links_with_a_date_or_departure_still_open_the_tour()
    {
        var (_, departureId, slug) = await PublishedTourAsync();

        foreach (var path in new[] { $"/tours/{slug}/2026-10-10/2", $"/tours/{slug}/{departureId}/3", $"/tours/{slug}/{departureId}" })
            (await app.Anonymous().GetAsync(path, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, path);
    }

    [Fact]
    public async Task An_event_booked_as_a_tour_shows_its_price_and_books_each_dates_departure()
    {
        var (tourId, departureId, slug) = await PublishedTourAsync();
        var api = await app.AdminApiAsync();
        var departureDay = TestBookings.BusinessNow.Date.AddDays(3); // the helper's departure day
        var name = $"Bar Hop {Guid.NewGuid():N}";
        await api.Send(new Fuse.Contracts.Events.SaveEvent(null, new Fuse.Contracts.Events.EventInput
        {
            Name = name, StartDate = departureDay.AddDays(-1).AddHours(18.5), PublishToSite = true, AllProperties = true, TourId = tourId,
            RecurrenceType = Shared.Events.EventRecurrenceType.Daily, RecurrenceInterval = 1, RecurrenceEndDate = departureDay,
        }, null, false), Ct);

        var ev = (await new Fuse.Web.Client.Api.HttpApi(app.Anonymous()).Send(new GetSiteEvent(Slugs.From(name)), Ct)).Event;

        ev.Booking.ShouldNotBeNull().TourSlug.ShouldBe(slug);
        ev.Booking.Price.ShouldBe(25m);
        ev.Booking.Online.ShouldBeTrue();
        ev.Upcoming.Single(d => d.Start.Date == departureDay).DepartureId.ShouldBe(departureId);
        ev.Upcoming.Single(d => d.Start.Date == departureDay).Price.ShouldBe(25m);

        // The calendar has the same price and departure.
        var calendar = await new Fuse.Web.Client.Api.HttpApi(app.Anonymous())
            .Send(new ListSiteEventOccurrences(departureDay, departureDay.AddDays(1), EventId: ev.Id), Ct);
        calendar.ShouldHaveSingleItem().Date.ShouldNotBeNull().Price.ShouldBe(25m);
        calendar[0].Date!.DepartureId.ShouldBe(departureId);
        calendar[0].Booking.ShouldNotBeNull().TourSlug.ShouldBe(slug);
        ev.Upcoming.Single(d => d.Start.Date == departureDay.AddDays(-1)).DepartureId.ShouldBeNull(); // no departure that day
        (await api.Send(new Fuse.Contracts.Events.GetEventEditor(ev.Id), Ct)).Details.TourId.ShouldBe(tourId);
    }

    [Fact]
    public async Task A_hidden_tour_is_bookable_only_through_a_published_event_and_stays_unlisted()
    {
        var (tourId, departureId, slug) = await PublishedTourAsync();
        await using (var scope = TestBookings.TenantScope(app))
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
                .ExecuteSqlRawAsync("UPDATE Catalog.Tours SET PublishToSite = 0 WHERE Id = {0}", new object[] { tourId }, Ct);
        var anonymous = new Fuse.Web.Client.Api.HttpApi(app.Anonymous());

        await Should.ThrowAsync<NotFoundException>(() => anonymous.Send(new GetSiteTour(slug), Ct).AsTask());
        (await anonymous.Send(new QuoteBasket([new BasketLineRequest(departureId, 1, null)]), Ct)).Lines[0].Problem.ShouldNotBeNull();

        var api = await app.AdminApiAsync();
        (await api.Send(new Fuse.Contracts.Events.ListEventTours(), Ct)).ShouldContain(t => t.Id == tourId.ToString() && t.Name.EndsWith("(not on website)"));
        await api.Send(new Fuse.Contracts.Events.SaveEvent(null, new Fuse.Contracts.Events.EventInput
        {
            Name = $"Bar Hop {Guid.NewGuid():N}", StartDate = TestBookings.BusinessNow.Date.AddDays(3).AddHours(18), PublishToSite = true, AllProperties = true, TourId = tourId,
        }, null, false), Ct);

        var tour = await anonymous.Send(new GetSiteTour(slug), Ct);
        tour.Listed.ShouldBeFalse();
        (await anonymous.Send(new ListSiteTourDepartures(tourId), Ct)).ShouldContain(d => d.Id == departureId);
        (await anonymous.Send(new QuoteBasket([new BasketLineRequest(departureId, 1, null)]), Ct)).Lines[0].Problem.ShouldBeNull();
        (await anonymous.Send(new ListSiteTours(), Ct)).ShouldNotContain(t => t.Id == tourId);
    }

    [Fact]
    public async Task An_event_following_its_tour_is_on_at_each_departure_less_the_days_hidden()
    {
        var (tourId, departureId, _) = await PublishedTourAsync();
        var api = await app.AdminApiAsync();
        var departure = TestBookings.BusinessNow.Date.AddDays(3).AddHours(9); // the helper's
        // A second departure a day later.
        var second = TestBookings.BusinessNow.Date.AddDays(4).AddHours(9);
        await using (var scope = TestBookings.TenantScope(app))
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(
                "INSERT INTO Catalog.TourDates (Id, TourId, TourPriceId, StartDate, EndDate, AvailableSpaces, TenantId, CreatedBy, CreatedOn, LastModifiedBy, ConcurrencyVersion) " +
                "SELECT NEWID(), TourId, TourPriceId, {0}, {1}, AvailableSpaces, TenantId, CreatedBy, CreatedOn, LastModifiedBy, ConcurrencyVersion FROM Catalog.TourDates WHERE Id = {2}",
                new object[] { second, second.AddHours(3), departureId }, Ct);
        var name = $"Bar Hop {Guid.NewGuid():N}";
        var id = await api.Send(new Fuse.Contracts.Events.SaveEvent(null, new Fuse.Contracts.Events.EventInput
        {
            Name = name, StartDate = TestBookings.BusinessNow.Date, PublishToSite = true, AllProperties = true, TourId = tourId, FollowsTourDates = true,
            CancelledDates = [second.Date],
        }, null, false), Ct);

        var ev = (await new Fuse.Web.Client.Api.HttpApi(app.Anonymous()).Send(new GetSiteEvent(Slugs.From(name)), Ct)).Event;

        ev.Upcoming.ShouldHaveSingleItem().Start.ShouldBe(departure); // the hidden day is left out
        ev.Upcoming[0].DepartureId.ShouldBe(departureId);
        (await api.Send(new Fuse.Contracts.Events.ListEventTourDepartures(tourId), Ct)).Select(d => d.Start).ShouldBe([departure, second]);
        var editor = await api.Send(new Fuse.Contracts.Events.GetEventEditor(id), Ct);
        (editor.Details.FollowsTourDates, editor.Details.CancelledDates.Single()).ShouldBe((true, second.Date));
    }

    [Fact]
    public async Task An_event_whose_tour_cannot_be_booked_online_offers_an_enquiry()
    {
        var (tourId, _, _) = await PublishedTourAsync();
        await using (var scope = TestBookings.TenantScope(app))
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
                .ExecuteSqlRawAsync("UPDATE Catalog.TourPrices SET PublishToWebsite = 0 WHERE TourId = {0}", new object[] { tourId }, Ct);
        var name = $"Bar Hop {Guid.NewGuid():N}";
        await (await app.AdminApiAsync()).Send(new Fuse.Contracts.Events.SaveEvent(null, new Fuse.Contracts.Events.EventInput
        {
            Name = name, StartDate = TestBookings.BusinessNow.Date.AddDays(3).AddHours(18), PublishToSite = true, AllProperties = true, TourId = tourId,
        }, null, false), Ct);

        var booking = (await new Fuse.Web.Client.Api.HttpApi(app.Anonymous()).Send(new GetSiteEvent(Slugs.From(name)), Ct)).Event.Booking;

        booking.ShouldNotBeNull();
        // Still priced (the tour's price), but booked by enquiry.
        (booking.Online, booking.Price, booking.TourId).ShouldBe((false, 25m, tourId));
        booking.TourName.ShouldNotBeNullOrEmpty();
    }
}
