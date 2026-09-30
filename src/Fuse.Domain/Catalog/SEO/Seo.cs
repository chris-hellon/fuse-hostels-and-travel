namespace Fuse.Domain.Catalog.SEO;

public class Seo : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private Seo() { }

    public string Breadcrumbs { get; set; }
    public string SearchAction { get; set; }

    public Seo(string breadcrumbs = "", string searchAction = "")
    {
        Breadcrumbs = breadcrumbs;
        SearchAction = searchAction;
    }

    public Seo Update(string breadcrumbs = "", string? searchAction = "")
    {
        if (!string.IsNullOrEmpty(breadcrumbs) && Breadcrumbs?.Equals(breadcrumbs) is not true) Breadcrumbs = breadcrumbs;
        if (!string.IsNullOrEmpty(searchAction) && SearchAction?.Equals(searchAction) is not true) SearchAction = searchAction;

        return this;
    }

    /// <summary>Sets both scripts (empty clears them; legacy Update ignored empty values).</summary>
    public void Set(string? breadcrumbs, string? searchAction)
    {
        Breadcrumbs = breadcrumbs?.Trim() ?? "";
        SearchAction = searchAction?.Trim() ?? "";
    }
}
