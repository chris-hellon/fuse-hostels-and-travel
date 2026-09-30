using Fuse.Contracts.Tours;

namespace Fuse.Contracts.Packages;

// Init-only properties: handlers project with object initializers so EF can sort on them.
public sealed record PackageSummary
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    public string? CategoryName { get; init; }
    public bool PublishToSite { get; init; }
    public int Tours { get; init; }
    /// <summary>Lowest and highest tier total (null when the package has no tiers).</summary>
    [NotSortable] public decimal? PriceLow { get; init; }
    [NotSortable] public decimal? PriceHigh { get; init; }
}

/// <summary>Back-office packages table.</summary>
[Post("packages/search"), Permission(FuseAction.Search, FuseResource.Packages)]
public sealed record SearchPackages : PagedQuery, IQuery<PagedResult<PackageSummary>>;

public sealed record PackageInput
{
    public string Name { get; set; } = "";
    public DefaultIdType? TourCategoryId { get; set; }
    /// <summary>Optional label next to the price, e.g. "from" or "per person".</summary>
    public string? PriceLabel { get; set; }
    /// <summary>Flat staff commission per guest, paid instead of the tours' own. Null = each tour's own rules.</summary>
    public decimal? CommissionAmount { get; set; }
    public bool CommissionIgnoresMinimumPax { get; set; }
    /// <summary>Staff-only notes.</summary>
    public string? Notes { get; set; }
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    /// <summary>Not editable for now (packages are sold by staff); kept as stored.</summary>
    public bool PublishToSite { get; set; }
}

/// <summary>A price tier (e.g. Adult, Child). <see cref="Key"/> is its id; new tiers get one from the editor.</summary>
public sealed record PackageTierEdit
{
    public DefaultIdType Key { get; init; }
    public string Title { get; init; } = "";
}

/// <summary>A tour in the package, in order. <see cref="Id"/> is null for a tour added in this edit.</summary>
public sealed record PackageItemEdit
{
    public DefaultIdType? Id { get; init; }
    public DefaultIdType TourId { get; init; }
    public int Quantity { get; init; } = 1;
    /// <summary>Minimum pax this tour must reach for the package commission to pay (null = none).</summary>
    public int? MinimumPax { get; init; }
    public CommissionScope? MinimumPaxScope { get; init; }
    /// <summary>This tour's package price per tier, keyed by tier key.</summary>
    public Dictionary<DefaultIdType, decimal> Prices { get; init; } = [];
}

/// <summary>Everything the package wizard edits; tiers and items are the complete wanted lists, in order.</summary>
public sealed record PackageChanges
{
    public PackageInput Details { get; init; } = new();
    public List<PackageTierEdit> Tiers { get; init; } = [];
    public List<PackageItemEdit> Items { get; init; } = [];
}

public sealed record PackageItemView
{
    public DefaultIdType Id { get; init; }
    public DefaultIdType TourId { get; init; }
    public string TourName { get; init; } = "";
    public int Quantity { get; init; }
    public int? MinimumPax { get; init; }
    public CommissionScope? MinimumPaxScope { get; init; }
    public Dictionary<DefaultIdType, decimal> Prices { get; init; } = [];
}

public sealed record PackageEditor
{
    public DefaultIdType Id { get; init; }
    public PackageInput Details { get; init; } = new();
    public string? ImagePath { get; init; }
    public IReadOnlyList<PackageTierEdit> Tiers { get; init; } = [];
    public IReadOnlyList<PackageItemView> Items { get; init; } = [];
}

[Get("packages/{Id}"), Permission(FuseAction.View, FuseResource.Packages)]
public sealed record GetPackageEditor(DefaultIdType Id) : IQuery<PackageEditor>;

/// <summary>Creates (no <see cref="Id"/>) or updates a package: details, tiers, tours and prices, image included, in one go.</summary>
[Put("packages"), Permission(FuseAction.Update, FuseResource.Packages)]
public sealed record SavePackage(DefaultIdType? Id, PackageChanges Changes, FileUpload? Image, bool RemoveImage) : ICommand<DefaultIdType>;

/// <summary>Bookings keep their own copy of the package, so a sold package can still be deleted.</summary>
[Delete("packages/{Id}"), Permission(FuseAction.Delete, FuseResource.Packages)]
public sealed record DeletePackage(DefaultIdType Id) : ICommand;

/// <summary>A tour that can go in a package, with its normal prices for reference.</summary>
public sealed record PackageTourOption
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Lowest and highest of the tour's prices (null when it has none).</summary>
    public decimal? PriceLow { get; init; }
    public decimal? PriceHigh { get; init; }
    /// <summary>A sensible starting package price: the lowest published price, else the lowest price.</summary>
    public decimal? DefaultPrice { get; init; }
}

[Get("packages/tour-options"), Permission(FuseAction.View, FuseResource.Packages)]
public sealed record ListPackageTourOptions : IQuery<IReadOnlyList<PackageTourOption>>;

public sealed class SavePackageValidator : AbstractValidator<SavePackage>
{
    public SavePackageValidator()
    {
        RuleFor(x => x.Changes.Details.Name).NotEmpty().MaximumLength(1024).WithName("Name");
        RuleFor(x => x.Changes.Details.CommissionAmount).GreaterThanOrEqualTo(0).When(x => x.Changes.Details.CommissionAmount.HasValue)
            .WithName("Package commission").WithMessage("Package commission can't be negative.");
        RuleFor(x => x.Changes.Tiers).NotEmpty().WithName("Tiers").WithMessage("Add at least one price tier.");
        RuleForEach(x => x.Changes.Tiers).Must(t => !string.IsNullOrWhiteSpace(t.Title)).WithName("Tiers").WithMessage("Each price tier needs a name.");
        RuleFor(x => x.Changes.Tiers).Must(t => t.Select(x => x.Key).Distinct().Count() == t.Count).WithName("Tiers").WithMessage("Each price tier needs its own key.");
        RuleFor(x => x.Changes.Items).Must(i => i.Count >= 2).WithName("Tours").WithMessage("A package needs at least two tours.");
        RuleForEach(x => x.Changes.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Quantity).GreaterThanOrEqualTo(1).WithMessage("Each tour needs a quantity of at least one.");
            item.RuleFor(i => i.MinimumPax).GreaterThanOrEqualTo(1).When(i => i.MinimumPax.HasValue).WithMessage("Minimum pax must be at least one.");
            item.RuleForEach(i => i.Prices).Must(p => p.Value >= 0).WithMessage("Prices can't be negative.");
        }).WithName("Tours");
        // As legacy: a package on the website needs an image (updates are checked against the saved image).
        RuleFor(x => x.Image).NotNull().When(x => x.Id is null && x.Changes.Details.PublishToSite)
            .WithName("Image").WithMessage("A package on the website needs an image.");
    }
}
