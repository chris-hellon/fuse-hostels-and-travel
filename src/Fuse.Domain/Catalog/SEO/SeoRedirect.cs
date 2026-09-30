namespace Fuse.Domain.Catalog.SEO;

public class SeoRedirect : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private SeoRedirect() { }

    public string Url { get; set; }
    public string RedirectUrl { get; set; }

    public SeoRedirect(string url, string redirectUrl)
    {
        Url = url;
        RedirectUrl = redirectUrl;
    }
    
    public SeoRedirect Update(string? url = null, string? redirectUrl = "")
    {
        if (!string.IsNullOrEmpty(url) && Url.Equals(url) is not true) Url = url;
        if (!string.IsNullOrEmpty(redirectUrl) && RedirectUrl.Equals(redirectUrl) is not true) RedirectUrl = redirectUrl;

        return this;
    }
}