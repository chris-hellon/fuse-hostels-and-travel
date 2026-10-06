using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Contracts.Events;
using Fuse.Contracts.Site;
using Fuse.Infrastructure.Persistence;
using Fuse.Site.Components.Events;

namespace Fuse.Web.Tests;

/// <summary>The public events calendar: the Every night strip's rule, and where each date is on.</summary>
public sealed class EventsCalendarTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime October = new(2026, 10, 1);

    private static IEnumerable<SiteEventOccurrence> On(DefaultIdType id, IEnumerable<DateTime> days, int hour = 19) =>
        days.Select(d => new SiteEventOccurrence { EventId = id, Name = "Event", Start = d.AddHours(hour), End = d.AddHours(hour + 2) });

    private static IEnumerable<DateTime> Days(DateTime from, int count, int every = 1) =>
        Enumerable.Range(0, count).Select(i => from.AddDays(i * every));

    [Fact]
    public void A_daily_event_is_nightly_even_with_several_times_a_day()
    {
        var bbq = DefaultIdType.NewGuid();
        var happyHour = DefaultIdType.NewGuid();
        var occurrences = On(bbq, Days(October, 31))
            .Concat(On(happyHour, Days(October, 31), 12)).Concat(On(happyHour, Days(October, 31), 17));

        NightlyEvents.In(occurrences, October).ShouldBe([bbq, happyHour], ignoreOrder: true);
    }

    [Fact]
    public void A_run_of_daily_dates_within_the_month_is_nightly_from_a_week_up()
    {
        var startsMidMonth = DefaultIdType.NewGuid(); // a daily event starting on the 20th
        var week = DefaultIdType.NewGuid();
        var shortRun = DefaultIdType.NewGuid(); // a six-day festival
        var occurrences = On(startsMidMonth, Days(October.AddDays(19), 12))
            .Concat(On(week, Days(October.AddDays(3), 7)))
            .Concat(On(shortRun, Days(October.AddDays(3), 6)));

        NightlyEvents.In(occurrences, October).ShouldBe([startsMidMonth, week], ignoreOrder: true);
    }

    [Fact]
    public void Weekly_and_gappy_events_stay_in_the_grid()
    {
        var weekly = DefaultIdType.NewGuid();
        var missesADay = DefaultIdType.NewGuid();
        var occurrences = On(weekly, Days(October, 5, every: 7))
            .Concat(On(missesADay, Days(October, 31).Where(d => d.Day != 15)));

        NightlyEvents.In(occurrences, October).ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_months_own_dates_count()
    {
        // Daily through September, then only the 1st and 2nd of October: two days in October isn't a week.
        var september = DefaultIdType.NewGuid();
        var occurrences = On(september, Days(October.AddDays(-30), 32));

        NightlyEvents.In(occurrences, October).ShouldBeEmpty();
        NightlyEvents.In(occurrences, October.AddMonths(-1)).ShouldBe([september]);
    }

    [Fact]
    public async Task Each_date_says_which_hostels_it_is_on_at()
    {
        await using (var scope = TestBookings.TenantScope(app))
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
                .ExecuteSqlRawAsync($"UPDATE Catalog.Properties SET PublishToSite = 1 WHERE Name = '{TestApp.SeedPropertyName}'", Ct);
        var api = await app.AdminApiAsync();
        var hostel = (await api.Send(new ListEventProperties(), Ct)).First(p => p.Name == TestApp.SeedPropertyName);
        var day = DateTime.Today.AddDays(4);
        var everywhere = await api.Send(new SaveEvent(null, new EventInput
        {
            Name = $"Bar Crawl {Guid.NewGuid():N}", StartDate = day.AddHours(21), PublishToSite = true, AllProperties = true,
        }, null, false), Ct);
        var atOne = await api.Send(new SaveEvent(null, new EventInput
        {
            Name = $"Pool Party {Guid.NewGuid():N}", StartDate = day.AddHours(14), PublishToSite = true, PropertyIds = [Guid.Parse(hostel.Id)],
        }, null, false), Ct);

        var anonymous = new Fuse.Web.Client.Api.HttpApi(app.Anonymous());
        var all = (await anonymous.Send(new ListSiteEventOccurrences(day, day.AddDays(1), EventId: everywhere), Ct)).ShouldHaveSingleItem();
        var one = (await anonymous.Send(new ListSiteEventOccurrences(day, day.AddDays(1), EventId: atOne), Ct)).ShouldHaveSingleItem();

        all.AllHostels.ShouldBeTrue();
        one.AllHostels.ShouldBeFalse();
        one.Hostels.ShouldBe([TestApp.SeedPropertyName]);
    }
}
