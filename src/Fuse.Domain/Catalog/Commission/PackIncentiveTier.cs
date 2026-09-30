namespace Fuse.Domain.Catalog.Commission;

/// <summary>
/// A named sale-price tier for a <see cref="PackIncentive"/> (e.g. Adult / Child), each with the pack's
/// total price for that tier. Mirrors package tiers, but a pack is priced as one bundle total per tier
/// (not per-tour), so the price lives directly on the tier.
/// </summary>
public class PackIncentiveTier : AuditableEntity, IAggregateRoot
{
    public DefaultIdType PackIncentiveId { get; set; }
    public string Title { get; set; } = null!;
    public int SortOrder { get; set; }
    public decimal Price { get; set; }

    public PackIncentiveTier()
    {
    }

    public PackIncentiveTier(string title, int sortOrder, decimal price, DefaultIdType? packIncentiveId = null)
    {
        Title = title;
        SortOrder = sortOrder;
        Price = price;

        if (packIncentiveId.HasValue)
        {
            PackIncentiveId = packIncentiveId.Value;
        }
    }

    public PackIncentiveTier Update(string? title, int? sortOrder, decimal? price)
    {
        if (title is not null && Title != title) Title = title;
        if (sortOrder.HasValue && SortOrder != sortOrder.Value) SortOrder = sortOrder.Value;
        if (price.HasValue && Price != price.Value) Price = price.Value;

        return this;
    }
}
