using Fuse.Domain.Catalog.Properties;

namespace Fuse.Domain.Catalog.Tours;

public class TourPropertyLookup : BaseEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private TourPropertyLookup() { }

    public TourPropertyLookup(DefaultIdType tourId, DefaultIdType propertyId)
    {
        TourId = tourId;
        PropertyId = propertyId;
    }

    public DefaultIdType TourId { get; private set; }
    public DefaultIdType PropertyId { get; private set; }

    public virtual Tour Tour { get; private set; } = null!;
    public virtual Property Property { get; private set; } = null!;
}