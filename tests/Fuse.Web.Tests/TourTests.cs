using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Admin.Pages.Tours;
using Fuse.Contracts.Api;
using Fuse.Contracts.Common;
using Fuse.Contracts.Locations;
using Fuse.Contracts.Tours;

namespace Fuse.Web.Tests;

/// <summary>
/// The tour wizard saves everything in one command (<see cref="TourChanges"/>). These tests drive
/// it the way the wizard does: load the editor, change a <see cref="TourDraft"/>, save.
/// </summary>
public sealed class TourTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Monday = NextMonday();
    private static readonly FileUpload Png = new("image.png", "image/png", [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);

    private static TourPriceInput PerPerson => new() { Title = "Per Person", Price = 25, HourDuration = 4 };

    /// <summary>A saved tour with one 4-hour price and, optionally, departures on it.</summary>
    private async Task<(IApi Api, TourDraft Draft)> NewTourAsync(int? maxCapacity = 10, Action<TourDraft>? addDates = null)
    {
        var api = await app.AdminApiAsync();
        var pickups = await api.Send(new ListPickupProperties(), Ct);
        var locations = await api.Send(new ListLocations(), Ct);

        var draft = TourDraft.New();
        draft.Details.Name = $"Test tour {Guid.NewGuid():N}";
        draft.Details.Description = "<p>Lovely</p>";
        draft.Details.HourDuration = "4";
        draft.Details.MaxCapacity = maxCapacity;
        draft.Details.LocationIds = [locations[0].Id];
        draft.Details.PickupPropertyIds = [Guid.Parse(pickups.Single(p => p.Name == TestApp.SeedPropertyName).Id)];
        draft.SavePrice(null, PerPerson);
        addDates?.Invoke(draft);

        var id = await api.Send(new CreateTour(draft.ToChanges()), Ct);
        return (api, await LoadAsync(api, id));
    }

    private async Task<TourDraft> LoadAsync(IApi api, DefaultIdType id) => TourDraft.From(await api.Send(new GetTourEditor(id), Ct));

    private Task SaveAsync(IApi api, TourDraft draft) => api.Send(new UpdateTour(draft.Id!.Value, draft.ToChanges()), Ct).AsTask();

    private static void AddWeekly(TourDraft draft, int weeks, params TimeOnly[] times) =>
        draft.AddDates(TourSchedule.Starts(Monday, Monday.AddDays(7 * weeks - 1), RepeatEvery.Week, 1, times), [draft.Prices[0].Key], null);

    [Fact]
    public async Task A_new_tour_is_saved_with_its_price_and_dates_in_one_go()
    {
        var (_, tour) = await NewTourAsync(addDates: d => AddWeekly(d, 4, new TimeOnly(9, 0), new TimeOnly(14, 0)));

        tour.Prices.ShouldHaveSingleItem().Details.Title.ShouldBe("Per Person");
        tour.Dates.Count.ShouldBe(8); // 4 Mondays x 2 times
        tour.Dates.ShouldAllBe(d => d.Original!.EndDate == d.StartDate.AddHours(4) && d.AvailableSpaces == 10 && !d.Booked);
    }

    [Fact]
    public async Task Departures_that_already_exist_are_not_added_twice()
    {
        var (api, tour) = await NewTourAsync(addDates: d => AddWeekly(d, 2, new TimeOnly(9, 0)));

        tour.AddDates([Monday.ToDateTime(new TimeOnly(9, 0))], [tour.Prices[0].Key], null).ShouldBe(0, "the draft skips a start it already has");
        await SaveAsync(api, tour);

        (await LoadAsync(api, tour.Id!.Value)).Dates.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Changing_capacity_moves_every_upcoming_departure()
    {
        var (api, tour) = await NewTourAsync(maxCapacity: 10, addDates: d => AddWeekly(d, 2, new TimeOnly(9, 0)));

        tour.Details.MaxCapacity = 16;
        await SaveAsync(api, tour);

        (await LoadAsync(api, tour.Id!.Value)).Dates.ShouldAllBe(d => d.AvailableSpaces == 16);
    }

    [Fact]
    public async Task Changing_a_price_duration_moves_upcoming_end_times()
    {
        var (api, tour) = await NewTourAsync(addDates: d => AddWeekly(d, 1, new TimeOnly(9, 0)));

        tour.SavePrice(tour.Prices[0].Key, PerPerson with { DayDuration = 1, HourDuration = 2 });
        await SaveAsync(api, tour);

        var date = (await LoadAsync(api, tour.Id!.Value)).Dates.Single();
        date.Original!.EndDate.ShouldBe(date.StartDate.AddDays(1).AddHours(2));
    }

    [Fact]
    public async Task Deleted_dates_are_only_removed_when_the_tour_is_saved()
    {
        var (api, tour) = await NewTourAsync(addDates: d => AddWeekly(d, 3, new TimeOnly(9, 0)));

        tour.DeleteDates([tour.Dates[0]]);
        (await LoadAsync(api, tour.Id!.Value)).Dates.Count.ShouldBe(3, "nothing changes until Save");

        await SaveAsync(api, tour);
        (await LoadAsync(api, tour.Id!.Value)).Dates.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Deleting_a_date_range_can_be_limited_to_one_start_time()
    {
        var (api, tour) = await NewTourAsync(addDates: d => AddWeekly(d, 3, new TimeOnly(9, 0), new TimeOnly(14, 0)));

        var morningsInFirstTwoWeeks = tour.DatesMatching(Monday, Monday.AddDays(13), new TimeOnly(9, 0));
        morningsInFirstTwoWeeks.Count.ShouldBe(2);
        tour.DeleteDates(morningsInFirstTwoWeeks);
        await SaveAsync(api, tour);

        var saved = await LoadAsync(api, tour.Id!.Value);
        saved.Dates.Count.ShouldBe(4);
        saved.Dates.Count(d => d.StartDate.Hour == 9).ShouldBe(1, "only the third week's morning is left");
    }

    [Fact]
    public async Task Removing_a_price_removes_its_dates_with_it()
    {
        var (api, tour) = await NewTourAsync(addDates: d => AddWeekly(d, 2, new TimeOnly(9, 0)));

        tour.RemovePrice(tour.Prices[0]).ShouldBeTrue();
        await SaveAsync(api, tour);

        var saved = await LoadAsync(api, tour.Id!.Value);
        saved.Prices.ShouldBeEmpty();
        saved.Dates.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_price_still_used_by_departures_cannot_be_removed_and_nothing_else_is_saved()
    {
        var (api, tour) = await NewTourAsync(addDates: d => AddWeekly(d, 3, new TimeOnly(9, 0)));
        var originalName = tour.Details.Name;

        // Rename and delete a date, but drop the price while its other dates still point at it.
        tour.Details.Name = originalName + " renamed";
        tour.DeleteDates([tour.Dates[0]]);
        var changes = tour.ToChanges() with { Prices = [] };
        await Should.ThrowAsync<BusinessRuleException>(() => api.Send(new UpdateTour(tour.Id!.Value, changes), Ct).AsTask());

        // One transaction: the rename and the deleted date didn't stick either.
        var reloaded = await LoadAsync(api, tour.Id!.Value);
        reloaded.Details.Name.ShouldBe(originalName);
        reloaded.Dates.Count.ShouldBe(3);
        reloaded.Prices.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Tour_names_are_unique()
    {
        var (api, tour) = await NewTourAsync();
        var copy = TourDraft.New();
        copy.Details.Name = tour.Details.Name;

        var failure = await Should.ThrowAsync<RequestValidationException>(() =>
            api.Send(new CreateTour(copy.ToChanges() with { Details = tour.Details }), Ct).AsTask());
        failure.Errors.Keys.ShouldContain(k => k.EndsWith("Name"));
    }

    [Fact]
    public async Task Banner_and_gallery_are_attached_on_save_and_kept_in_order()
    {
        var (api, tour) = await NewTourAsync();
        var banner = await api.Send(new UploadTourImage(Png with { FileName = "banner.png" }), Ct);
        var first = await api.Send(new UploadTourImage(Png with { FileName = "a.png" }), Ct);
        var second = await api.Send(new UploadTourImage(Png with { FileName = "b.png" }), Ct);

        tour.SetBanner(banner);
        tour.AddGalleryImage(first);
        tour.AddGalleryImage(second);
        tour.MoveGalleryImage(0, 1);
        (await LoadAsync(api, tour.Id!.Value)).Gallery.ShouldBeEmpty("uploads aren't attached until Save");

        await SaveAsync(api, tour);
        var saved = await api.Send(new GetTourEditor(tour.Id!.Value), Ct);
        saved.ImagePath.ShouldNotBeNull().ShouldEndWith("?w=1000");
        saved.Gallery.Count.ShouldBe(2);
        saved.Gallery[0].ImagePath.ShouldStartWith(second.Split('?')[0]);

        // Removing one image and saving keeps the other.
        var reloaded = TourDraft.From(saved);
        reloaded.RemoveGalleryImage(reloaded.Gallery[0]);
        await SaveAsync(api, reloaded);
        (await api.Send(new GetTourEditor(tour.Id!.Value), Ct)).Gallery.ShouldHaveSingleItem().ImagePath.ShouldStartWith(first.Split('?')[0]);
    }

    [Fact]
    public async Task Replaced_and_removed_images_stay_in_storage_as_legacy()
    {
        var (api, tour) = await NewTourAsync();
        var oldBanner = await api.Send(new UploadTourImage(Png with { FileName = "old.png" }), Ct);
        var galleryImage = await api.Send(new UploadTourImage(Png with { FileName = "g.png" }), Ct);
        tour.SetBanner(oldBanner);
        tour.AddGalleryImage(galleryImage);
        await SaveAsync(api, tour);

        var reloaded = await LoadAsync(api, tour.Id!.Value);
        reloaded.SetBanner(await api.Send(new UploadTourImage(Png with { FileName = "new.png" }), Ct));
        reloaded.RemoveGalleryImage(reloaded.Gallery[0]);
        await SaveAsync(api, reloaded);

        (await LoadAsync(api, tour.Id!.Value)).Gallery.ShouldBeEmpty();
        File.Exists(StoredFile(oldBanner)).ShouldBeTrue("the replaced banner may be used elsewhere on the site");
        File.Exists(StoredFile(galleryImage)).ShouldBeTrue("the removed slideshow image may be used elsewhere on the site");
    }

    private string StoredFile(string url)
    {
        var root = Infrastructure.Files.LocalFileStore.Root(app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>());
        return Path.Combine(root, url.Split('?')[0][(Infrastructure.Files.LocalFileStore.RequestPath.Length + 1)..]);
    }

    [Fact]
    public async Task Saving_a_tour_keeps_its_address()
    {
        var (api, tour) = await NewTourAsync();
        await using (var scope = TestBookings.TenantScope(app))
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.AppDbContext>();
            await db.Tours.Where(t => t.Id == tour.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Address, "126 Nguyen Truong To"), Ct);
        }

        tour.Details.ShortDescription = "<p>Changed</p>";
        await SaveAsync(api, tour);

        await using var check = TestBookings.TenantScope(app);
        var saved = check.ServiceProvider.GetRequiredService<Infrastructure.Persistence.AppDbContext>();
        (await saved.Tours.Where(t => t.Id == tour.Id).Select(t => t.Address).SingleAsync(Ct)).ShouldBe("126 Nguyen Truong To");
    }

    [Fact]
    public async Task The_tours_seo_page_takes_the_uploaded_banner_and_keeps_its_scripts()
    {
        var api = await app.AdminApiAsync();
        var draft = TourDraft.New();
        draft.Details.Name = $"Seo tour {Guid.NewGuid():N}";
        draft.Details.Description = "<p>Tour</p>";
        draft.Details.HourDuration = "4";
        draft.Details.LocationIds = [(await api.Send(new ListLocations(), Ct))[0].Id];
        draft.Details.PickupPropertyIds = [Guid.Parse((await api.Send(new ListPickupProperties(), Ct)).Single(p => p.Name == TestApp.SeedPropertyName).Id)];
        draft.SavePrice(null, PerPerson);
        var banner = await api.Send(new UploadTourImage(Png with { FileName = "seo.png" }), Ct);
        draft.SetBanner(banner);
        var id = await api.Send(new CreateTour(draft.ToChanges()), Ct);
        var title = $"Tours - {draft.Details.Name}";

        async Task<(string? Image, string? Scripts)> PageAsync()
        {
            await using var scope = TestBookings.TenantScope(app);
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.AppDbContext>();
            var page = await db.Pages.AsNoTracking().SingleAsync(p => p.Title == title, Ct);
            return (page.MetaImageUrl, page.CustomSeoScripts);
        }

        (await PageAsync()).ShouldBe((banner, ""));

        // A save without a new banner leaves the image; scripts are kept (legacy reset them to ""; the user decided otherwise).
        await using (var scope = TestBookings.TenantScope(app))
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.AppDbContext>();
            await db.Pages.Where(p => p.Title == title).ExecuteUpdateAsync(s => s.SetProperty(p => p.CustomSeoScripts, "<script></script>"), Ct);
        }
        var tour = await LoadAsync(api, id);
        await SaveAsync(api, tour);
        (await PageAsync()).ShouldBe((banner, "<script></script>"));

        var newBanner = await api.Send(new UploadTourImage(Png with { FileName = "seo2.png" }), Ct);
        tour = await LoadAsync(api, id);
        tour.SetBanner(newBanner);
        await SaveAsync(api, tour);
        (await PageAsync()).ShouldBe((newBanner, "<script></script>"));
    }

    [Fact]
    public async Task Images_from_anywhere_else_are_refused()
    {
        var (api, tour) = await NewTourAsync();

        tour.SetBanner("https://example.invalid/assets/images/evil.png");
        await Should.ThrowAsync<RequestValidationException>(() => SaveAsync(api, tour));

        await Should.ThrowAsync<RequestValidationException>(() =>
            api.Send(new UploadTourImage(Png with { FileName = "virus.exe" }), Ct).AsTask());
    }

    private static DateOnly NextMonday()
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(7));
        while (day.DayOfWeek != DayOfWeek.Monday)
            day = day.AddDays(1);
        return day;
    }

    [Fact]
    public async Task A_price_without_a_duration_takes_the_tours_and_its_departures_end_by_it()
    {
        var api = await app.AdminApiAsync();
        var draft = TourDraft.New();
        draft.Details.Name = $"Bar hop {Guid.NewGuid():N}";
        draft.Details.Description = "<p>Tour</p>";
        draft.Details.HourDuration = "3";
        draft.Details.LocationIds = [(await api.Send(new ListLocations(), Ct))[0].Id];
        draft.Details.PickupPropertyIds = [Guid.Parse((await api.Send(new ListPickupProperties(), Ct)).Single(p => p.Name == TestApp.SeedPropertyName).Id)];
        // As legacy saved some: a duration of 0 days.
        draft.SavePrice(null, new TourPriceInput { Title = "Ticket", Price = 6.5m, DayDuration = 0, PublishToWebsite = true });
        var start = TestBookings.BusinessNow.Date.AddDays(5).AddHours(18.5);
        draft.AddDates([start], [draft.Prices[0].Key], null);

        var tourId = await api.Send(new CreateTour(draft.ToChanges()), Ct);

        await using var scope = TestBookings.TenantScope(app);
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.AppDbContext>();
        (await db.TourPrices.Where(p => p.TourId == tourId).Select(p => p.HourDuration).SingleAsync(Ct)).ShouldBe(3m);
        (await db.TourDates.Where(d => d.TourId == tourId).Select(d => d.EndDate).SingleAsync(Ct)).ShouldBe(start.AddHours(3));
    }
}
