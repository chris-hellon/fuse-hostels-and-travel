namespace Fuse.Domain.Catalog.Packages;

/// <summary>
/// The price of one package item (tour) for one tier. This is the source of truth for pricing:
/// the package total for a tier is the sum of its items' prices for that tier × quantity.
/// </summary>
public class PackageItemPrice : AuditableEntity, IAggregateRoot
{
    public DefaultIdType PackageItemId { get; set; }
    public DefaultIdType PackageTierId { get; set; }

    /// <summary>The price for this tour under this tier (per person).</summary>
    public decimal Price { get; set; }

    public virtual PackageTier? PackageTier { get; set; }

    public PackageItemPrice()
    {
    }

    public PackageItemPrice(DefaultIdType packageTierId, decimal price, DefaultIdType? packageItemId = null)
    {
        PackageTierId = packageTierId;
        Price = price;

        if (packageItemId.HasValue)
        {
            PackageItemId = packageItemId.Value;
        }
    }

    public PackageItemPrice Update(decimal? price)
    {
        if (price.HasValue && Price != price.Value) Price = price.Value;

        return this;
    }
}
