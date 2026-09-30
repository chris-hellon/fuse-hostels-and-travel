using Shouldly;
using Fuse.Contracts.Api;
using Fuse.Contracts.Common;
using Fuse.Contracts.Events;
using Fuse.Contracts.Locations;
using Fuse.Contracts.Properties;
using Fuse.Shared.Events;

namespace Fuse.Web.Tests;

public sealed class EventTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly FileUpload Png = new("image.png", "image/png", [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);

    // A Monday well in the future, so the dates never fall outside the expander's one-year cap in a way that changes.
    private static DateTime NextMonday(int weeksAhead = 1)
    {
        var d = DateTime.Today.AddDays(1);
        while (d.DayOfWeek != DayOfWeek.Monday)
            d = d.AddDays(1);
        return d.AddDays(7 * (weeksAhead - 1));
    }

    private async Task<DefaultIdType> NewPropertyAsync(IApi api)
    {
        var banner = await api.Send(new UploadPropertyMedia(Png, IsVideo: false), Ct);
        return await api.Send(new CreateProperty(new PropertyChanges
        {
            Details = new PropertyInput
            {
                Name = $"Hostel {Guid.NewGuid():N}", AddressLine1 = "1 Beach Road", TelephoneNumber = "+84 1",
                LocationId = (await api.Send(new ListLocations(), Ct))[0].Id, Description = "<p>Hostel</p>",
            },
            Banner = banner,
        }), Ct);
    }

    private static EventInput Weekly(DateTime monday) => new()
    {
        Name = $"Pub Quiz {Guid.NewGuid():N}",
        StartDate = monday.AddHours(19),
        EndDate = monday.AddHours(21),
        RecurrenceType = EventRecurrenceType.Weekly,
        RecurrenceDaysOfWeek = (1 << (int)DayOfWeek.Monday) | (1 << (int)DayOfWeek.Wednesday),
        RecurrenceEndDate = monday.AddDays(20),
        Colour = "#48bb78",
        PublishToSite = true,
        ShortDescription = "<p>Every Monday and Wednesday</p>",
    };

    [Fact]
    public async Task An_event_is_saved_with_its_schedule_properties_cancelled_dates_and_image()
    {
        var api = await app.AdminApiAsync();
        var property = await NewPropertyAsync(api);
        var monday = NextMonday();
        var input = Weekly(monday) with { PropertyIds = [property], CancelledDates = [monday.AddDays(2)] };

        var id = await api.Send(new SaveEvent(null, input, Png, false), Ct);
        var editor = await api.Send(new GetEventEditor(id), Ct);

        editor.Details.Colour.ShouldBe("#48bb78");
        editor.Details.PropertyIds.ShouldBe([property]);
        editor.Details.CancelledDates.ShouldBe([monday.AddDays(2)]);
        editor.Details.RecurrenceDaysOfWeek.ShouldBe(input.RecurrenceDaysOfWeek);
        editor.ImagePath.ShouldNotBeNull().ShouldEndWith("?w=1000");

        var row = (await api.Send(new SearchEvents { Keyword = input.Name }, Ct)).Items.ShouldHaveSingleItem();
        row.Colour.ShouldBe("#48bb78");
        row.Repeats.ShouldStartWith("Every week on Mon, Wed until");
    }

    [Fact]
    public async Task Occurrences_follow_the_rule_and_skip_cancelled_dates()
    {
        var api = await app.AdminApiAsync();
        var monday = NextMonday(2);
        var input = Weekly(monday) with { CancelledDates = [monday.AddDays(7)] };
        var id = await api.Send(new SaveEvent(null, input, null, false), Ct);

        var dates = (await api.Send(new ListEventOccurrences(monday, monday.AddDays(30)), Ct))
            .Where(o => o.EventId == id).Select(o => o.Start).ToList();

        // Mon, Wed for three weeks up to the end date (Mon + 20 days = the third Sunday), less the cancelled second Monday.
        dates.ShouldBe([
            monday.AddHours(19), monday.AddDays(2).AddHours(19),
            monday.AddDays(9).AddHours(19),
            monday.AddDays(14).AddHours(19), monday.AddDays(16).AddHours(19),
        ]);
        (await api.Send(new ListEventOccurrences(monday, monday.AddDays(30)), Ct)).First(o => o.EventId == id).End.ShouldBe(monday.AddHours(21));
    }

    [Fact]
    public async Task Editing_reconciles_properties_and_a_one_off_keeps_no_cancelled_dates()
    {
        var api = await app.AdminApiAsync();
        var first = await NewPropertyAsync(api);
        var second = await NewPropertyAsync(api);
        var monday = NextMonday();
        var id = await api.Send(new SaveEvent(null, Weekly(monday) with { PropertyIds = [first], CancelledDates = [monday] }, null, false), Ct);
        var editor = await api.Send(new GetEventEditor(id), Ct);

        await api.Send(new SaveEvent(id, editor.Details with
        {
            PropertyIds = [second],
            RecurrenceType = EventRecurrenceType.None,
            Colour = null,
        }, null, false), Ct);

        var saved = await api.Send(new GetEventEditor(id), Ct);
        saved.Details.PropertyIds.ShouldBe([second]);
        saved.Details.CancelledDates.ShouldBeEmpty();
        saved.Details.RecurrenceDaysOfWeek.ShouldBeNull();
        saved.Details.Colour.ShouldBeNull();

        await api.Send(new SaveEvent(id, saved.Details with { AllProperties = true }, null, false), Ct);
        (await api.Send(new GetEventEditor(id), Ct)).Details.PropertyIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Bad_schedules_and_links_are_refused()
    {
        var api = await app.AdminApiAsync();
        var input = Weekly(NextMonday());

        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SaveEvent(null, input with { EndDate = input.StartDate.AddHours(-1) }, null, false), Ct).AsTask());
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SaveEvent(null, input with { RecurrenceEndDate = input.StartDate.AddDays(-1) }, null, false), Ct).AsTask());
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SaveEvent(null, input with { ExternalUrl = "facebook.com/events/1" }, null, false), Ct).AsTask());
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SaveEvent(null, input with { ShortDescription = new string('x', 501) }, null, false), Ct).AsTask());
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SaveEvent(null, input with { EventCategoryId = Guid.NewGuid() }, null, false), Ct).AsTask());
    }

    [Fact]
    public async Task A_category_with_events_cannot_be_deleted()
    {
        var api = await app.AdminApiAsync();
        var name = $"Live Music {Guid.NewGuid():N}";
        var category = await api.Send(new SaveEventCategory(null, new EventCategoryInput { Name = name, Colour = "#f56565", SortOrder = 5 }), Ct);
        var @event = await api.Send(new SaveEvent(null, Weekly(NextMonday()) with { EventCategoryId = category, Colour = null }, null, false), Ct);

        var row = (await api.Send(new SearchEventCategories { Keyword = name }, Ct)).Items.ShouldHaveSingleItem();
        row.Events.ShouldBe(1);
        (await api.Send(new SearchEvents { EventCategoryId = category }, Ct)).Items.ShouldHaveSingleItem().Colour.ShouldBe("#f56565");
        await Should.ThrowAsync<BusinessRuleException>(() => api.Send(new DeleteEventCategory(category), Ct).AsTask());
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SaveEventCategory(null, new EventCategoryInput { Name = name }), Ct).AsTask());

        await api.Send(new DeleteEvent(@event), Ct);
        await api.Send(new DeleteEventCategory(category), Ct);
        (await api.Send(new SearchEventCategories { Keyword = name }, Ct)).Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("2026-01-31", 4, "2026-01-31,2026-02-28,2026-03-31,2026-04-30")]
    [InlineData("2026-03-15", 3, "2026-03-15,2026-04-15,2026-05-15")]
    public void Monthly_rules_keep_the_day_of_the_month(string start, int count, string expected)
    {
        var from = DateTime.Parse(start);
        var dates = EventRecurrence.Expand(EventRecurrenceType.Monthly, from, null, 1, null, null, from, from.AddMonths(count - 1).AddDays(1), today: from);
        dates.Select(d => d.ToString("yyyy-MM-dd")).ShouldBe(expected.Split(','));
    }

    [Theory]
    [InlineData("#48bb78", "72, 187, 120")]
    [InlineData("#000000", null)]
    [InlineData("", null)]
    public void Colours_round_trip_the_legacy_format(string hex, string? triplet)
    {
        EventColours.ToRgbTriplet(hex).ShouldBe(triplet);
        if (triplet is not null)
            EventColours.ToHex(triplet).ShouldBe(hex);
    }

    [Theory]
    [InlineData(EventRecurrenceType.None, 1, 0, null)]
    [InlineData(EventRecurrenceType.Daily, 1, 0, "Every day")]
    [InlineData(EventRecurrenceType.Daily, 3, 0, "Every 3 days")]
    [InlineData(EventRecurrenceType.Weekly, 1, 0, "Every Thu")]
    [InlineData(EventRecurrenceType.Weekly, 1, (1 << 1) | (1 << 3), "Every Mon & Wed")]
    [InlineData(EventRecurrenceType.Weekly, 1, (1 << 1) | (1 << 3) | (1 << 5), "Every Mon, Wed & Fri")]
    [InlineData(EventRecurrenceType.Weekly, 1, 127, "Every day")]
    [InlineData(EventRecurrenceType.Weekly, 2, 1 << 5, "Every 2 weeks on Fri")]
    [InlineData(EventRecurrenceType.Monthly, 1, 0, "Monthly on the 1st")]
    public void Visitors_see_a_short_repeat_rule(EventRecurrenceType type, int interval, int days, string? expected) =>
        // 1 Oct 2026 is a Thursday.
        EventRecurrence.Summary(type, new DateTime(2026, 10, 1, 19, 0, 0), interval, days == 0 ? null : days).ShouldBe(expected);

    [Fact]
    public async Task An_event_on_several_times_a_day_is_on_once_per_time_each_ending_at_its_own_time()
    {
        var api = await app.AdminApiAsync();
        var monday = NextMonday();
        var input = Weekly(monday) with
        {
            TimeSlots =
            [
                new EventTimeSlotInput { StartTime = new TimeSpan(17, 0, 0), EndTime = new TimeSpan(18, 0, 0) },
                new EventTimeSlotInput { StartTime = new TimeSpan(12, 0, 0), EndTime = new TimeSpan(13, 0, 0) },
                new EventTimeSlotInput { StartTime = new TimeSpan(19, 0, 0) },
            ],
        };
        var id = await api.Send(new SaveEvent(null, input, null, false), Ct);

        // Saved in time order.
        (await api.Send(new GetEventEditor(id), Ct)).Details.TimeSlots.Select(t => t.StartTime.Hours).ShouldBe([12, 17, 19]);

        var onMonday = (await api.Send(new ListEventOccurrences(monday, monday.AddDays(1)), Ct)).Where(o => o.EventId == id).ToList();
        onMonday.Select(o => (o.Start.Hour, o.End.Hour)).ShouldBe([(12, 13), (17, 18), (19, 19)]);

        // All day: the times go.
        await api.Send(new SaveEvent(id, input with { AllDay = true }, null, false), Ct);
        (await api.Send(new GetEventEditor(id), Ct)).Details.TimeSlots.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_same_time_twice_or_a_time_ending_before_it_starts_is_refused()
    {
        var api = await app.AdminApiAsync();
        var twice = Weekly(NextMonday()) with
        {
            TimeSlots = [new EventTimeSlotInput { StartTime = new TimeSpan(17, 0, 0) }, new EventTimeSlotInput { StartTime = new TimeSpan(17, 0, 0) }],
        };
        var backwards = Weekly(NextMonday()) with
        {
            TimeSlots = [new EventTimeSlotInput { StartTime = new TimeSpan(17, 0, 0), EndTime = new TimeSpan(16, 0, 0) }],
        };

        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SaveEvent(null, twice, null, false), Ct).AsTask());
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SaveEvent(null, backwards, null, false), Ct).AsTask());
    }

    [Fact]
    public void Several_times_a_day_repeat_on_every_day_the_rule_lands_on_except_cancelled_ones()
    {
        var start = new DateTime(2026, 10, 5, 18, 0, 0); // a Monday
        var dates = EventRecurrence.Expand(EventRecurrenceType.Daily, start, null, 1, null, [start.AddDays(1)],
            start.Date, start.Date.AddDays(3).AddHours(23), today: start.Date, timesOfDay: [new TimeSpan(19, 0, 0), new TimeSpan(12, 0, 0)]);

        dates.Select(d => d.ToString("ddd HH")).ShouldBe(["Mon 12", "Mon 19", "Wed 12", "Wed 19", "Thu 12", "Thu 19"]);
    }

    [Theory]
    [InlineData("209, 172, 0", "#ff66cc", "#d1ac00")] // its own colour wins
    [InlineData("#48bb78", "#ff66cc", "#48bb78")]
    [InlineData(null, "#ff66cc", "#ff66cc")] // else the category's
    [InlineData(null, null, null)]
    public void An_events_own_colour_comes_before_its_categorys(string? own, string? category, string? expected) =>
        EventColours.For(own, category).ShouldBe(expected);
}
