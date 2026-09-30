using Fuse.Domain.Catalog.Tours;

namespace Fuse.Domain.Catalog.Packages;

public class PackageItem : AuditableEntity, IAggregateRoot
{
    public DefaultIdType PackageId { get; set; }
    public DefaultIdType TourId { get; set; }

    /// <summary>Position of this tour within the package, for display ordering.</summary>
    public int SortOrder { get; set; }

    /// <summary>How many of this tour are included in the package.</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>Optional pin to a specific tour price tier used when the tour is booked.</summary>
    public DefaultIdType? TourPriceId { get; set; }

    /// <summary>Minimum pax this tour must reach for the package commission to pay. Null = no minimum for this
    /// tour. Applied per tour: the package commission pays only when every tour in the package meets its own.</summary>
    public int? MinimumPax { get; set; }

    /// <summary>How <see cref="MinimumPax"/> is counted: 0 = per departure (start time), 1 = per day.</summary>
    public int? MinimumPaxScope { get; set; }

    public virtual Tour? Tour { get; set; }

    /// <summary>The price of this tour per package tier. Source of truth for pricing.</summary>
    public virtual IList<PackageItemPrice>? PackageItemPrices { get; set; }

    public PackageItem()
    {
    }

    public PackageItem(DefaultIdType tourId, int sortOrder, int quantity, DefaultIdType? tourPriceId = null, DefaultIdType? packageId = null,
        int? minimumPax = null, int? minimumPaxScope = null)
    {
        TourId = tourId;
        SortOrder = sortOrder;
        Quantity = quantity < 1 ? 1 : quantity;
        TourPriceId = tourPriceId;
        MinimumPax = minimumPax;
        MinimumPaxScope = minimumPaxScope;

        if (packageId.HasValue)
        {
            PackageId = packageId.Value;
        }
    }

    public PackageItem Update(DefaultIdType? tourId, int? sortOrder, int? quantity, DefaultIdType? tourPriceId,
        int? minimumPax = null, int? minimumPaxScope = null)
    {
        if (tourId.HasValue && tourId.Value != DefaultIdType.Empty && !TourId.Equals(tourId.Value)) TourId = tourId.Value;
        if (sortOrder.HasValue && SortOrder != sortOrder.Value) SortOrder = sortOrder.Value;
        if (quantity.HasValue && quantity.Value >= 1 && Quantity != quantity.Value) Quantity = quantity.Value;
        TourPriceId = tourPriceId;
        MinimumPax = minimumPax;
        MinimumPaxScope = minimumPaxScope;

        return this;
    }

    /// <summary>The price of this tour for the given tier (0 if not set). Requires prices to be loaded.</summary>
    public decimal PriceForTier(DefaultIdType tierId) =>
        PackageItemPrices?.FirstOrDefault(p => p.PackageTierId == tierId)?.Price ?? 0m;
}
