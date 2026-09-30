namespace Fuse.Domain.Catalog.Packages;

/// <summary>
/// A named pricing tier for a package (e.g. "Adult", "Child", "Standard"). Each tier defines a
/// column of per-tour prices via <see cref="PackageItemPrice"/>.
/// </summary>
public class PackageTier : AuditableEntity, IAggregateRoot
{
    public DefaultIdType PackageId { get; set; }
    public string Title { get; set; } = null!;
    public int SortOrder { get; set; }

    public PackageTier()
    {
    }

    public PackageTier(DefaultIdType id, string title, int sortOrder, DefaultIdType? packageId = null)
    {
        Id = id;
        Title = title;
        SortOrder = sortOrder;

        if (packageId.HasValue)
        {
            PackageId = packageId.Value;
        }
    }

    public PackageTier Update(string? title, int? sortOrder)
    {
        if (title is not null && !Title.Equals(title)) Title = title;
        if (sortOrder.HasValue && SortOrder != sortOrder.Value) SortOrder = sortOrder.Value;

        return this;
    }
}
