namespace Fuse.Domain.Catalog.Properties;

public class PropertyDirectionContent : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private PropertyDirectionContent() { }

    public PropertyDirectionContent(string body, string? style)
    {
        Body = body.NormalizeRichText() ?? string.Empty;
        Style = style;
    }

    public DefaultIdType PropertyDirectionId { get; private set; }
    public string Body { get; private set; }
    public string? Style { get; private set; }
    public virtual PropertyDirection PropertyDirection { get; private set; } = null!;

    public static PropertyDirectionContent Create(DefaultIdType directionId, string body) =>
        new() { PropertyDirectionId = directionId, Body = body.NormalizeRichText() ?? string.Empty };

    public PropertyDirectionContent Update(string? body, string? style)
    {
        if (body is not null && Body.Equals(body) is not true) Body = body.NormalizeRichText() ?? string.Empty;
        if (style is not null && Style?.Equals(style) is not true) Style = style;
        return this;
    }
}