namespace Fuse.Domain.Catalog.Events;

/// <summary>A single cancelled occurrence of a recurring event — the expander skips this date.</summary>
public class EventOccurrenceException : BaseEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private EventOccurrenceException() { }

    public EventOccurrenceException(DefaultIdType eventId, DateTime occurrenceDate)
    {
        EventId = eventId;
        OccurrenceDate = occurrenceDate.Date;
    }

    public DefaultIdType EventId { get; private set; }

    /// <summary>The date (time ignored) of the occurrence that is cancelled.</summary>
    public DateTime OccurrenceDate { get; private set; }

    public virtual Event Event { get; private set; } = null!;
}
