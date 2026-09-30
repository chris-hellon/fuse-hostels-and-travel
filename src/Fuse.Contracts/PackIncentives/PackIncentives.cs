using Fuse.Contracts.Tours;

namespace Fuse.Contracts.PackIncentives;

public sealed record PackIncentiveSummary
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    public int MinimumProductCount { get; init; }
    public decimal CommissionAmount { get; init; }
    public int Tours { get; init; }
    public bool IsActive { get; init; }
}

[Post("pack-incentives/search"), Permission(FuseAction.Search, FuseResource.PackIncentives)]
public sealed record SearchPackIncentives : PagedQuery, IQuery<PagedResult<PackIncentiveSummary>>;

public sealed record PackIncentiveInput
{
    public string Name { get; set; } = "";
    /// <summary>How many distinct eligible tours a booking needs to qualify.</summary>
    public int MinimumProductCount { get; set; } = 3;
    /// <summary>Flat commission per guest when the pack qualifies.</summary>
    public decimal CommissionAmount { get; set; }
    /// <summary>Pay even if the tours miss their own minimum pax (they must still run).</summary>
    public bool IgnoresMinimumPax { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

/// <summary>A sale-price tier (e.g. Adult): the pack's total price for it. <see cref="Id"/> is null for a new tier.</summary>
public sealed record PackIncentiveTierEdit
{
    public DefaultIdType? Id { get; init; }
    public string Title { get; init; } = "";
    public decimal Price { get; init; }
}

public sealed record PackIncentiveEditor
{
    public DefaultIdType Id { get; init; }
    public PackIncentiveInput Details { get; init; } = new();
    public IReadOnlyList<LookupItem> Tours { get; init; } = [];
    public IReadOnlyList<PackIncentiveTierEdit> Tiers { get; init; } = [];
}

[Get("pack-incentives/{Id}"), Permission(FuseAction.View, FuseResource.PackIncentives)]
public sealed record GetPackIncentiveEditor(DefaultIdType Id) : IQuery<PackIncentiveEditor>;

/// <summary>Creates (no <see cref="Id"/>) or updates a pack incentive with its eligible tours and tiers, in one go.</summary>
[Put("pack-incentives"), Permission(FuseAction.Update, FuseResource.PackIncentives)]
public sealed record SavePackIncentive(DefaultIdType? Id, PackIncentiveInput Details, List<DefaultIdType> TourIds, List<PackIncentiveTierEdit> Tiers)
    : ICommand<DefaultIdType>;

[Delete("pack-incentives/{Id}"), Permission(FuseAction.Delete, FuseResource.PackIncentives)]
public sealed record DeletePackIncentive(DefaultIdType Id) : ICommand;

/// <summary>Every tour, for choosing eligible tours.</summary>
[Get("pack-incentives/tour-options"), Permission(FuseAction.View, FuseResource.PackIncentives)]
public sealed record ListPackIncentiveTourOptions : IQuery<IReadOnlyList<LookupItem>>;

public sealed class SavePackIncentiveValidator : AbstractValidator<SavePackIncentive>
{
    public SavePackIncentiveValidator()
    {
        RuleFor(x => x.Details.Name).NotEmpty().MaximumLength(200).WithName("Name");
        RuleFor(x => x.Details.MinimumProductCount).GreaterThanOrEqualTo(1).WithName("Minimum products").WithMessage("Minimum products must be at least 1.");
        RuleFor(x => x.Details.CommissionAmount).GreaterThanOrEqualTo(0).WithName("Commission").WithMessage("Commission can't be negative.");
        RuleFor(x => x.TourIds).Must((x, tours) => tours.Distinct().Count() >= x.Details.MinimumProductCount).WithName("Tours")
            .WithMessage(x => $"Choose at least {x.Details.MinimumProductCount} eligible tour{(x.Details.MinimumProductCount == 1 ? "" : "s")} to match the minimum.");
        RuleForEach(x => x.Tiers).ChildRules(tier =>
        {
            tier.RuleFor(t => t.Title).NotEmpty().WithMessage("Each price tier needs a name.");
            tier.RuleFor(t => t.Price).GreaterThanOrEqualTo(0).WithMessage("Prices can't be negative.");
        }).WithName("Tiers");
    }
}
