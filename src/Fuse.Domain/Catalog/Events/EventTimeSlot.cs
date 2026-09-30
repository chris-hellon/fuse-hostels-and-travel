namespace Fuse.Domain.Catalog.Events;

/// <summary>
/// A time of day the event runs at. An event with several slots (e.g. Happy Hour at 12:00, 17:00 and 19:00) is on
/// once per slot on every day its rule lands on. No slots = the event's own start time.
/// </summary>
public class EventTimeSlot : BaseEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private EventTimeSlot() { }

    public EventTimeSlot(DefaultIdType eventId, TimeSpan startTime, TimeSpan? endTime, int sortOrder)
    {
        EventId = eventId;
        StartTime = startTime;
        EndTime = endTime;
        SortOrder = sortOrder;
    }

    public DefaultIdType EventId { get; private set; }

    /// <summary>Time of day the slot starts.</summary>
    public TimeSpan StartTime { get; private set; }

    /// <summary>Time of day the slot ends (optional; null = a point in time).</summary>
    public TimeSpan? EndTime { get; private set; }

    /// <summary>Order of the slot within the day.</summary>
    public int SortOrder { get; private set; }

    public virtual Event Event { get; private set; } = null!;
}
