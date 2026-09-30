using Shouldly;
using Fuse.Contracts.Common;
using Fuse.Contracts.PackIncentives;

namespace Fuse.Web.Tests;

public sealed class PackIncentiveTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PackIncentiveInput Pack(int minimum) => new() { Name = $"Pack {Guid.NewGuid():N}", MinimumProductCount = minimum, CommissionAmount = 6 };

    [Fact]
    public async Task A_pack_needs_at_least_its_minimum_number_of_tours()
    {
        var api = await app.AdminApiAsync();
        var tours = (await api.Send(new ListPackIncentiveTourOptions(), Ct)).Select(t => Guid.Parse(t.Id)).ToList();
        tours.Count.ShouldBeGreaterThanOrEqualTo(1, "the test database needs a tour");

        var failure = await Should.ThrowAsync<RequestValidationException>(() =>
            api.Send(new SavePackIncentive(null, Pack(minimum: tours.Count + 1), tours, []), Ct).AsTask());
        failure.Errors.Keys.ShouldContain(k => k.EndsWith("TourIds"));
    }

    [Fact]
    public async Task A_pack_is_saved_with_its_tours_and_tiers_and_can_be_edited()
    {
        var api = await app.AdminApiAsync();
        var tour = Guid.Parse((await api.Send(new ListPackIncentiveTourOptions(), Ct))[0].Id);

        var id = await api.Send(new SavePackIncentive(null, Pack(1), [tour],
            [new() { Title = "Adult", Price = 60 }, new() { Title = "Child", Price = 40 }]), Ct);
        var editor = await api.Send(new GetPackIncentiveEditor(id), Ct);
        editor.Tours.ShouldHaveSingleItem().Id.ShouldBe(tour.ToString());
        editor.Tiers.Select(t => (t.Title, t.Price)).ShouldBe([("Adult", 60m), ("Child", 40m)]);

        // Drop Child, reprice Adult, turn the pack off.
        await api.Send(new SavePackIncentive(id, editor.Details with { IsActive = false }, [tour],
            [new() { Id = editor.Tiers[0].Id, Title = "Adult", Price = 55 }]), Ct);
        var saved = await api.Send(new GetPackIncentiveEditor(id), Ct);
        saved.Tiers.ShouldHaveSingleItem().Price.ShouldBe(55);
        saved.Details.IsActive.ShouldBeFalse();

        // Add a tier and a tour to the saved pack.
        var extra = Admin.Pages.Tours.TourDraft.New();
        extra.Details.Name = $"Pack tour {Guid.NewGuid():N}";
        extra.Details.Description = "<p>Tour</p>";
        extra.Details.HourDuration = "2";
        extra.Details.LocationIds = [(await api.Send(new Fuse.Contracts.Locations.ListLocations(), Ct))[0].Id];
        var pickups = await api.Send(new Fuse.Contracts.Tours.ListPickupProperties(), Ct);
        extra.Details.PickupPropertyIds = [Guid.Parse(pickups.Single(p => p.Name == TestApp.SeedPropertyName).Id)];
        var second = await api.Send(new Fuse.Contracts.Tours.CreateTour(extra.ToChanges()), Ct);
        await api.Send(new SavePackIncentive(id, saved.Details, [tour, second],
            [new() { Id = saved.Tiers[0].Id, Title = "Adult", Price = 55 }, new() { Title = "Student", Price = 45 }]), Ct);
        var grown = await api.Send(new GetPackIncentiveEditor(id), Ct);
        grown.Tiers.Select(t => t.Title).ShouldBe(["Adult", "Student"]);
        grown.Tours.Count.ShouldBe(2);

        await api.Send(new DeletePackIncentive(id), Ct);
        (await api.Send(new SearchPackIncentives { PageSize = 500 }, Ct)).Items.ShouldNotContain(p => p.Id == id);
    }
}
