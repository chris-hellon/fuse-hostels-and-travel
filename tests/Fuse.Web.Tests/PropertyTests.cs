using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Contracts.Api;
using Fuse.Contracts.Common;
using Fuse.Contracts.Locations;
using Fuse.Contracts.Properties;
using Fuse.Contracts.Tours;

namespace Fuse.Web.Tests;

public sealed class PropertyTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly FileUpload Png = new("image.png", "image/png", [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);

    private async Task<(IApi Api, PropertyChanges Changes)> NewPropertyAsync()
    {
        var api = await app.AdminApiAsync();
        var location = (await api.Send(new ListLocations(), Ct))[0].Id;
        var banner = await api.Send(new UploadPropertyMedia(Png, IsVideo: false), Ct);
        var roomImage = await api.Send(new UploadPropertyMedia(Png, IsVideo: false), Ct);
        return (api, new PropertyChanges
        {
            Details = new PropertyInput
            {
                Name = $"Hostel {Guid.NewGuid():N}", AddressLine1 = "1 Beach Road", TelephoneNumber = "+84 1", LocationId = location,
                Description = "<p>Lovely hostel</p>", H1 = "Heading",
            },
            Banner = banner,
            Rooms = [new() { Name = "Dorm", Description = "<p>8 beds</p>", ImageUrl = roomImage }, new() { Name = "Private", Description = "<p>Double</p>" }],
            Directions = [new() { Title = "From the airport", Body = "<p>Taxi 20 minutes</p>" }],
        });
    }

    [Fact]
    public async Task A_property_is_saved_with_its_rooms_directions_location_and_media()
    {
        var (api, changes) = await NewPropertyAsync();

        var id = await api.Send(new CreateProperty(changes), Ct);
        var editor = await api.Send(new GetPropertyEditor(id), Ct);

        editor.Details.LocationId.ShouldBe(changes.Details.LocationId);
        editor.Details.H1.ShouldBe("Heading");
        editor.ImagePath.ShouldNotBeNull().ShouldEndWith("?w=1000");
        editor.Rooms.Select(r => r.Name).ShouldBe(["Dorm", "Private"]);
        editor.Rooms[0].ImageUrl.ShouldNotBeNull();
        editor.Directions.ShouldHaveSingleItem().Body.ShouldBe("<p>Taxi 20 minutes</p>");
        var row = (await api.Send(new SearchProperties { Keyword = changes.Details.Name }, Ct)).Items.ShouldHaveSingleItem();
        row.Rooms.ShouldBe(2);
        row.LocationName.ShouldNotBeNull();
    }

    [Fact]
    public async Task Editing_reconciles_rooms_directions_and_the_gallery()
    {
        var (api, changes) = await NewPropertyAsync();
        var id = await api.Send(new CreateProperty(changes), Ct);
        var editor = await api.Send(new GetPropertyEditor(id), Ct);
        var slide = await api.Send(new UploadPropertyMedia(Png, IsVideo: false), Ct);

        await api.Send(new UpdateProperty(id, new PropertyChanges
        {
            Details = editor.Details,
            Banner = editor.ImagePath,
            Rooms = [editor.Rooms[0] with { Name = "Dorm (8 beds)", ImageUrl = null }],
            Directions = [editor.Directions[0] with { Body = "<p>Grab</p>" }, new() { Title = "From the bus station", Body = "<p>Walk</p>" }],
            Gallery = [new GalleryRef(null, slide)],
        }), Ct);

        var saved = await api.Send(new GetPropertyEditor(id), Ct);
        saved.Rooms.ShouldHaveSingleItem().Name.ShouldBe("Dorm (8 beds)");
        saved.Rooms[0].ImageUrl.ShouldBeNull();
        saved.Directions.Select(d => d.Body).ShouldBe(["<p>Grab</p>", "<p>Walk</p>"]);
        saved.Gallery.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Saving_a_property_keeps_its_seo_pages_scripts()
    {
        var (api, changes) = await NewPropertyAsync();
        var id = await api.Send(new CreateProperty(changes), Ct);
        var title = $"Hostels - {changes.Details.Name}";
        await using (var scope = TestBookings.TenantScope(app))
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.AppDbContext>();
            await db.Pages.Where(p => p.Title == title).ExecuteUpdateAsync(s => s.SetProperty(p => p.CustomSeoScripts, "<script></script>"), Ct);
        }

        var editor = await api.Send(new GetPropertyEditor(id), Ct);
        await api.Send(new UpdateProperty(id, new PropertyChanges
        {
            Details = editor.Details, Banner = editor.ImagePath, Rooms = [.. editor.Rooms], Directions = [.. editor.Directions],
        }), Ct);

        await using var check = TestBookings.TenantScope(app);
        var saved = check.ServiceProvider.GetRequiredService<Infrastructure.Persistence.AppDbContext>();
        (await saved.Pages.AsNoTracking().SingleAsync(p => p.Title == title, Ct)).CustomSeoScripts.ShouldBe("<script></script>");
    }

    [Fact]
    public async Task A_property_needs_a_banner_and_distinct_room_names()
    {
        var (api, changes) = await NewPropertyAsync();

        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new CreateProperty(changes with { Banner = null }), Ct).AsTask());
        await Should.ThrowAsync<RequestValidationException>(() =>
            api.Send(new CreateProperty(changes with { Rooms = [changes.Rooms[0], changes.Rooms[0] with { ImageUrl = null }] }), Ct).AsTask());
        await Should.ThrowAsync<RequestValidationException>(() =>
            api.Send(new CreateProperty(changes with { Banner = "https://example.invalid/assets/images/x.png" }), Ct).AsTask());
    }

    [Fact]
    public async Task A_property_tours_pick_up_from_cannot_be_deleted()
    {
        var (api, changes) = await NewPropertyAsync();
        var id = await api.Send(new CreateProperty(changes), Ct);
        var tour = Admin.Pages.Tours.TourDraft.New();
        tour.Details.Name = $"Tour {Guid.NewGuid():N}";
        tour.Details.Description = "<p>Tour</p>";
        tour.Details.HourDuration = "2";
        tour.Details.LocationIds = [changes.Details.LocationId!.Value];
        tour.Details.PickupPropertyIds = [id];
        var tourId = await api.Send(new CreateTour(tour.ToChanges()), Ct);

        await Should.ThrowAsync<BusinessRuleException>(() => api.Send(new DeleteProperty(id), Ct).AsTask());

        await api.Send(new DeleteTour(tourId), Ct);
        await api.Send(new DeleteProperty(id), Ct);
        (await api.Send(new SearchProperties { Keyword = changes.Details.Name }, Ct)).Items.ShouldBeEmpty();
    }
}
