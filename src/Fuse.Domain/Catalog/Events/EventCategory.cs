namespace Fuse.Domain.Catalog.Events;

/// <summary>A staff-managed event type (e.g. Pub Crawl, Live Music), with a colour used on the calendar.</summary>
public class EventCategory : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private EventCategory() { }

    public string Name { get; private set; } = null!;

    /// <summary>Hex colour (e.g. "#48bb78") used for this category on the calendar. Falls back per-event.</summary>
    public string? Color { get; private set; }

    public int SortOrder { get; private set; }

    public virtual IList<Event> Events { get; set; } = new List<Event>();

    public static EventCategory Create(string name, string? colour, int sortOrder)
    {
        var category = new EventCategory();
        category.Apply(name, colour, sortOrder);
        return category;
    }

    public void Apply(string name, string? colour, int sortOrder)
    {
        Name = name.Trim();
        Color = string.IsNullOrWhiteSpace(colour) ? null : colour.Trim();
        SortOrder = sortOrder;
    }
}
