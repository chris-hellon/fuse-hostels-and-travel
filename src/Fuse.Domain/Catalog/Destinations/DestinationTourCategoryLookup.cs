using Fuse.Domain.Catalog.Tours;

namespace Fuse.Domain.Catalog.Destinations;

public class DestinationTourCategoryLookup : BaseEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private DestinationTourCategoryLookup() { }

    public DestinationTourCategoryLookup(DefaultIdType destinationId, DefaultIdType tourCategoryId)
    {
        DestinationId = destinationId;
        TourCategoryId = tourCategoryId;
    }

    public DefaultIdType DestinationId { get; private set; }
    public DefaultIdType TourCategoryId { get; private set; }

    public virtual Destination Destination { get; private set; } = null!;
    public virtual TourCategory TourCategory { get; private set; } = null!;
}