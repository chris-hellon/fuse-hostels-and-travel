using Fuse.Contracts.Api;
using Fuse.Contracts.Locations;
using Fuse.Contracts.Tours;

namespace Fuse.Admin.Pages.Tours;

/// <summary>Everything the wizard's pick lists need, loaded once (in parallel) when it opens.</summary>
public sealed record TourLookups(
    IReadOnlyList<TourCategoryItem> Categories,
    IReadOnlyList<LocationSummary> Locations,
    IReadOnlyList<LookupItem> Pickups,
    IReadOnlyList<LookupItem> Suppliers)
{
    public static async Task<TourLookups> LoadAsync(IApi api)
    {
        var categories = api.Send(new ListTourCategories()).AsTask();
        var locations = api.Send(new ListLocations()).AsTask();
        var pickups = api.Send(new ListPickupProperties()).AsTask();
        var suppliers = api.Send(new ListSuppliers()).AsTask();
        await Task.WhenAll(categories, locations, pickups, suppliers);
        return new(categories.Result, locations.Result, pickups.Result, suppliers.Result);
    }
}
