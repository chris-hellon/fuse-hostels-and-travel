using Fuse.Domain.Catalog.Destinations;

namespace Fuse.Domain.Catalog.Tours;

public class TourDestinationLookup : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private TourDestinationLookup() { }

    public TourDestinationLookup(DefaultIdType tourId, DefaultIdType destinationId)
    {
        TourId = tourId;
        DestinationId = destinationId;
    }

    public DefaultIdType TourId { get; private set; }
    public DefaultIdType DestinationId { get; private set; }

    public virtual Tour Tour { get; private set; } = null!;
    public virtual Destination Destination { get; private set; } = null!;
}