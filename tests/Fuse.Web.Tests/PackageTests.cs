using Shouldly;
using Fuse.Admin.Pages.Tours;
using Fuse.Contracts.Api;
using Fuse.Contracts.Common;
using Fuse.Contracts.Locations;
using Fuse.Contracts.Packages;
using Fuse.Contracts.Tours;

namespace Fuse.Web.Tests;

public sealed class PackageTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(IApi Api, DefaultIdType TourA, DefaultIdType TourB)> TwoToursAsync()
    {
        var api = await app.AdminApiAsync();
        var pickups = await api.Send(new ListPickupProperties(), Ct);
        var locations = await api.Send(new ListLocations(), Ct);
        async Task<DefaultIdType> TourAsync(string name)
        {
            var tour = TourDraft.New();
            tour.Details.Name = $"{name} {Guid.NewGuid():N}";
            tour.Details.Description = "<p>Tour</p>";
            tour.Details.HourDuration = "2";
            tour.Details.LocationIds = [locations[0].Id];
            tour.Details.PickupPropertyIds = [Guid.Parse(pickups.Single(p => p.Name == TestApp.SeedPropertyName).Id)];
            tour.SavePrice(null, new TourPriceInput { Title = "Per person", Price = 20, HourDuration = 2, PublishToWebsite = true });
            return await api.Send(new CreateTour(tour.ToChanges()), Ct);
        }
        return (api, await TourAsync("Boat"), await TourAsync("Cooking"));
    }

    private static PackageChanges Combo(DefaultIdType tourA, DefaultIdType tourB, DefaultIdType adult, DefaultIdType child) => new()
    {
        Details = new PackageInput { Name = $"Combo {Guid.NewGuid():N}", PriceLabel = "per person" },
        Tiers = [new() { Key = adult, Title = "Adult" }, new() { Key = child, Title = "Child" }],
        Items =
        [
            new() { TourId = tourA, Quantity = 1, Prices = new() { [adult] = 15, [child] = 10 } },
            new() { TourId = tourB, Quantity = 2, Prices = new() { [adult] = 12, [child] = 8 } },
        ],
    };

    [Fact]
    public async Task A_package_is_saved_with_its_tiers_tours_and_prices()
    {
        var (api, tourA, tourB) = await TwoToursAsync();
        var (adult, child) = (Guid.NewGuid(), Guid.NewGuid());

        var id = await api.Send(new SavePackage(null, Combo(tourA, tourB, adult, child), null, false), Ct);
        var editor = await api.Send(new GetPackageEditor(id), Ct);

        editor.Tiers.Select(t => t.Title).ShouldBe(["Adult", "Child"]);
        editor.Items.Select(i => i.TourId).ShouldBe([tourA, tourB]);
        editor.Items[1].Quantity.ShouldBe(2);
        editor.Items[1].Prices[child].ShouldBe(8);

        // Totals: Adult 15 + 2 × 12 = 39; Child 10 + 2 × 8 = 26.
        var row = (await api.Send(new SearchPackages { Keyword = "Combo", PageSize = 100 }, Ct)).Items.Single(p => p.Id == id);
        (row.PriceLow, row.PriceHigh, row.Tours).ShouldBe((26m, 39m, 2));
    }

    [Fact]
    public async Task Editing_reorders_removes_and_reprices_in_one_save()
    {
        var (api, tourA, tourB) = await TwoToursAsync();
        var (adult, child) = (Guid.NewGuid(), Guid.NewGuid());
        var id = await api.Send(new SavePackage(null, Combo(tourA, tourB, adult, child), null, false), Ct);
        var editor = await api.Send(new GetPackageEditor(id), Ct);
        var (_, tourC, _) = await TwoToursAsync();

        // Drop the Child tier, swap the order, replace nothing, add a third tour.
        var changes = new PackageChanges
        {
            Details = editor.Details with { Name = editor.Details.Name + " edited" },
            Tiers = [new() { Key = adult, Title = "Everyone" }],
            Items =
            [
                new() { Id = editor.Items[1].Id, TourId = tourB, Quantity = 1, Prices = new() { [adult] = 11 } },
                new() { Id = editor.Items[0].Id, TourId = tourA, Quantity = 1, Prices = new() { [adult] = 15 } },
                new() { TourId = tourC, Quantity = 1, Prices = new() { [adult] = 5 } },
            ],
        };
        await api.Send(new SavePackage(id, changes, null, false), Ct);

        var saved = await api.Send(new GetPackageEditor(id), Ct);
        saved.Tiers.ShouldHaveSingleItem().Title.ShouldBe("Everyone");
        saved.Items.Select(i => i.TourId).ShouldBe([tourB, tourA, tourC]);
        saved.Items.ShouldAllBe(i => i.Prices.Count == 1 && i.Prices.ContainsKey(adult));
        (await api.Send(new SearchPackages { Keyword = "edited", PageSize = 100 }, Ct)).Items.Single(p => p.Id == id).PriceLow.ShouldBe(31m);
    }

    [Fact]
    public async Task A_tier_added_to_a_saved_package_saves_with_its_prices()
    {
        var (api, tourA, tourB) = await TwoToursAsync();
        var (adult, child) = (Guid.NewGuid(), Guid.NewGuid());
        var id = await api.Send(new SavePackage(null, Combo(tourA, tourB, adult, child), null, false), Ct);

        // As the wizard does: load, add a tier in the tiers dialog, price it, save.
        var draft = Admin.Pages.Packages.PackageDraft.From(await api.Send(new GetPackageEditor(id), Ct));
        var student = Guid.NewGuid();
        draft.SetTiers([.. draft.Tiers, new Admin.Pages.Packages.TierDraft(student, "Student")]);
        foreach (var item in draft.Items)
            item.Prices[student] = 9;
        await api.Send(new SavePackage(id, draft.ToChanges(), null, false), Ct);

        var saved = await api.Send(new GetPackageEditor(id), Ct);
        saved.Tiers.Select(t => t.Title).ShouldBe(["Adult", "Child", "Student"]);
        var studentKey = saved.Tiers.Single(t => t.Title == "Student").Key;
        saved.Items.ShouldAllBe(i => i.Prices[studentKey] == 9);
    }

    [Fact]
    public async Task A_package_needs_two_tours_named_tiers_and_a_unique_name()
    {
        var (api, tourA, tourB) = await TwoToursAsync();
        var tier = Guid.NewGuid();
        var changes = Combo(tourA, tourB, tier, Guid.NewGuid());

        var oneTour = changes with { Items = [changes.Items[0]] };
        (await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SavePackage(null, oneTour, null, false), Ct).AsTask()))
            .Errors.Keys.ShouldContain(k => k.Contains("Items"));

        var unnamedTier = changes with { Tiers = [new() { Key = tier, Title = " " }] };
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SavePackage(null, unnamedTier, null, false), Ct).AsTask());

        await api.Send(new SavePackage(null, changes, null, false), Ct);
        (await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SavePackage(null, changes, null, false), Ct).AsTask()))
            .Errors.Keys.ShouldContain(k => k.EndsWith("Name"));
    }

    [Fact]
    public async Task Deleted_packages_disappear_from_the_list()
    {
        var (api, tourA, tourB) = await TwoToursAsync();
        var id = await api.Send(new SavePackage(null, Combo(tourA, tourB, Guid.NewGuid(), Guid.NewGuid()), null, false), Ct);

        await api.Send(new DeletePackage(id), Ct);

        (await api.Send(new SearchPackages { PageSize = 500 }, Ct)).Items.ShouldNotContain(p => p.Id == id);
    }

    [Fact]
    public async Task Tour_options_carry_normal_prices()
    {
        var (api, tourA, _) = await TwoToursAsync();

        var option = (await api.Send(new ListPackageTourOptions(), Ct)).Single(t => t.Id == tourA);

        (option.PriceLow, option.PriceHigh, option.DefaultPrice).ShouldBe((20m, 20m, 20m));
    }
}
