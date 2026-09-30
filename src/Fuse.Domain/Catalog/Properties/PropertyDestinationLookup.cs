using Fuse.Domain.Catalog.Destinations;

namespace Fuse.Domain.Catalog.Properties;

public class PropertyDestinationLookup : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private PropertyDestinationLookup() { }

    public PropertyDestinationLookup(DefaultIdType propertyId, DefaultIdType destinationId)
    {
        PropertyId = propertyId;
        DestinationId = destinationId;
    }

    public DefaultIdType PropertyId { get; private set; }
    public DefaultIdType DestinationId { get; private set; }

    public virtual Property Property { get; private set; } = null!;
    public virtual Destination Destination { get; private set; } = null!;
}