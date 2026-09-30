using Fuse.Domain.Catalog.Properties;

namespace Fuse.Domain.Catalog.Tours;

public class TourPickupLocation : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private TourPickupLocation() { }

    public DefaultIdType TourId { get; set; }
    public DefaultIdType PropertyId { get; set; }

    public virtual Property Property { get; set; } = null!;

    public TourPickupLocation(DefaultIdType tourId, DefaultIdType propertyId)
    {
        TourId = tourId;
        PropertyId = propertyId;
    }
}