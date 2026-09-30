namespace Fuse.Domain.Catalog.Pages;

public class PageModalLookup : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private PageModalLookup() { }

    public PageModalLookup(DefaultIdType id, DefaultIdType pageId, DefaultIdType pageModalId)
    {
        Id = id;
        PageId = pageId;
        PageModalId = pageModalId;
    }

    public DefaultIdType PageId { get; set; }
    public DefaultIdType PageModalId { get; set; }

    public virtual Page Page { get; set; } = null!;
    public virtual PageModal PageModal { get; set; } = null!;
}