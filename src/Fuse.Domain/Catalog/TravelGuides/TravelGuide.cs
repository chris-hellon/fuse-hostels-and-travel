namespace Fuse.Domain.Catalog.TravelGuides;

public class TravelGuide : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private TravelGuide() { }

    public string Title { get; private set; } = null!;
    public string SubTitle { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public string ShortDescription { get; private set; } = null!;
    public string? ImagePath { get; private set; }
    public string? MetaKeywords { get; private set; }
    public string? MetaDescription { get; private set; }
    public string? UrlFriendlyTitle { get; private set; }

    public virtual IEnumerable<TravelGuideGalleryImage>? TravelGuideGalleryImages { get; set; }

    public static TravelGuide Create(TravelGuideDetails details)
    {
        var guide = new TravelGuide();
        guide.Apply(details);
        return guide;
    }

    public void Apply(TravelGuideDetails details)
    {
        Title = details.Title.Trim();
        SubTitle = details.SubTitle.Trim();
        ShortDescription = details.ShortDescription.NormalizeRichText() ?? string.Empty;
        Description = details.Description.NormalizeRichText() ?? string.Empty;
        UrlFriendlyTitle = details.UrlSlug.Trim();
        MetaKeywords = string.IsNullOrWhiteSpace(details.MetaKeywords) ? null : details.MetaKeywords.Trim();
        MetaDescription = string.IsNullOrWhiteSpace(details.MetaDescription) ? null : details.MetaDescription.Trim();
    }

    /// <summary>Stored as given (legacy guides have no width hint).</summary>
    public void SetImage(string? url) => ImagePath = url;
}
public sealed record TravelGuideDetails
{
    public required string Title { get; init; }
    public required string SubTitle { get; init; }
    public required string ShortDescription { get; init; }
    public required string Description { get; init; }
    public required string UrlSlug { get; init; }
    public string? MetaKeywords { get; init; }
    public string? MetaDescription { get; init; }
}
