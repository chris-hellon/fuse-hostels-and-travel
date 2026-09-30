using Shouldly;
using Fuse.Admin.Pages.Tours;
using Fuse.Contracts.Common;
using Fuse.Contracts.Locations;
using Fuse.Contracts.Tours;

namespace Fuse.Web.Tests;

public sealed class TourCategoryTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly FileUpload Png = new("category.png", "image/png", [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);

    private static TourCategoryInput NewCategory() => new() { Name = $"Category {Guid.NewGuid():N}", ShortDescription = "<p>Short</p>" };

    [Fact]
    public async Task A_new_category_needs_an_image()
    {
        var api = await app.AdminApiAsync();

        var failure = await Should.ThrowAsync<RequestValidationException>(() =>
            api.Send(new SaveTourCategory(null, NewCategory(), Image: null, RemoveImage: false), Ct).AsTask());
        failure.Errors.Keys.ShouldContain(k => k.EndsWith("Image"));
    }

    [Fact]
    public async Task A_category_is_saved_with_its_image_and_can_be_edited_without_one()
    {
        var api = await app.AdminApiAsync();
        var input = NewCategory();

        var id = await api.Send(new SaveTourCategory(null, input, Png, RemoveImage: false), Ct);
        var saved = await api.Send(new GetTourCategory(id), Ct);
        saved.ImagePath.ShouldNotBeNull().ShouldEndWith("?w=1000");
        saved.Details.ShortDescription.ShouldBe("<p>Short</p>");

        // Editing keeps the image unless a new one is chosen or it's removed.
        await api.Send(new SaveTourCategory(id, input with { Name = input.Name + " renamed" }, Image: null, RemoveImage: false), Ct);
        (await api.Send(new GetTourCategory(id), Ct)).ImagePath.ShouldBe(saved.ImagePath);

        await api.Send(new SaveTourCategory(id, input, Image: null, RemoveImage: true), Ct);
        (await api.Send(new GetTourCategory(id), Ct)).ImagePath.ShouldBeNull();
    }

    [Fact]
    public async Task Category_names_are_unique()
    {
        var api = await app.AdminApiAsync();
        var input = NewCategory();
        await api.Send(new SaveTourCategory(null, input, Png, RemoveImage: false), Ct);

        var failure = await Should.ThrowAsync<RequestValidationException>(() =>
            api.Send(new SaveTourCategory(null, input, Png, RemoveImage: false), Ct).AsTask());
        failure.Errors.Keys.ShouldContain(k => k.EndsWith("Name"));
    }

    [Fact]
    public async Task A_category_with_tours_cannot_be_deleted()
    {
        var api = await app.AdminApiAsync();
        var categoryId = await api.Send(new SaveTourCategory(null, NewCategory(), Png, RemoveImage: false), Ct);

        var pickups = await api.Send(new ListPickupProperties(), Ct);
        var locations = await api.Send(new ListLocations(), Ct);
        var tour = TourDraft.New();
        tour.Details.Name = $"Tour {Guid.NewGuid():N}";
        tour.Details.Description = "<p>Lovely</p>";
        tour.Details.HourDuration = "2";
        tour.Details.TourCategoryId = categoryId;
        tour.Details.LocationIds = [locations[0].Id];
        tour.Details.PickupPropertyIds = [Guid.Parse(pickups.Single(p => p.Name == TestApp.SeedPropertyName).Id)];
        var tourId = await api.Send(new CreateTour(tour.ToChanges()), Ct);

        (await api.Send(new SearchTourCategories { Keyword = "Category", PageSize = 500 }, Ct)).Items.Single(c => c.Id == categoryId).Tours.ShouldBe(1);
        await Should.ThrowAsync<BusinessRuleException>(() => api.Send(new DeleteTourCategory(categoryId), Ct).AsTask());

        await api.Send(new DeleteTour(tourId), Ct);
        await api.Send(new DeleteTourCategory(categoryId), Ct);
        (await api.Send(new ListTourCategories(), Ct)).ShouldNotContain(c => c.Id == categoryId);
    }
}
