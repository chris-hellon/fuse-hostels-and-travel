namespace Fuse.Domain.Catalog.Properties;

public class PropertyDirection : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private PropertyDirection() { }

    public PropertyDirection(string title)
    {
        Title = title;
    }

    public DefaultIdType PropertyId { get; private set; }
    public string Title { get; private set; }

    public virtual IList<PropertyDirectionContent> Content { get; set; } = null!;

    public static PropertyDirection Create(DefaultIdType propertyId, string title) =>
        new() { PropertyId = propertyId, Title = title.Trim(), Content = [] };

    public PropertyDirection Update(string title)
    {
        if (title is not null && Title?.Equals(title) is not true) Title = title;
        return this;
    }
}