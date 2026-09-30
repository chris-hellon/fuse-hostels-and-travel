using Fuse.Domain.Catalog.Properties;

namespace Fuse.Domain.Catalog.Events;

/// <summary>Assigns an event to a hostel (many-to-many). An event with <c>AllProperties</c> set applies to every hostel.</summary>
public class EventPropertyLookup : BaseEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private EventPropertyLookup() { }

    public EventPropertyLookup(DefaultIdType eventId, DefaultIdType propertyId)
    {
        EventId = eventId;
        PropertyId = propertyId;
    }

    public DefaultIdType EventId { get; private set; }
    public DefaultIdType PropertyId { get; private set; }

    public virtual Event Event { get; private set; } = null!;
    public virtual Property Property { get; private set; } = null!;
}
