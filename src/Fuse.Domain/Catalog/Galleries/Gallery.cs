namespace Fuse.Domain.Catalog.Galleries;

public class Gallery : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private Gallery() { }

    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? MetaKeywords { get; private set; }
    public string? MetaDescription { get; private set; }

    public virtual IList<GalleryImage> GalleryImages { get; set; } = null!;

    public Gallery(string title, string? description, string? metaKeywords, string? metaDescription)
    {
        Title = title;
        Description = description;
        MetaKeywords = metaKeywords;
        MetaDescription = metaDescription;
    }
    
    public Gallery Update(string? title, string? description, string? metaKeywords, string? metaDescription)
    {
        if (title is not null && Title != title)
            Title = title;

        if (description is not null && Description != description)
            Description = description;

        if (metaKeywords is not null && MetaKeywords != metaKeywords)
            MetaKeywords = metaKeywords;
        
        if (metaDescription is not null && MetaDescription != metaDescription)
            MetaDescription = metaDescription;
        
        return this;
    }

    public static Gallery Create(string title, string? description, string? metaKeywords, string? metaDescription)
    {
        var gallery = new Gallery();
        gallery.Apply(title, description, metaKeywords, metaDescription);
        return gallery;
    }

    public void Apply(string title, string? description, string? metaKeywords, string? metaDescription)
    {
        Title = title.Trim();
        Description = description.NormalizeRichText();
        MetaKeywords = string.IsNullOrWhiteSpace(metaKeywords) ? null : metaKeywords.Trim();
        MetaDescription = string.IsNullOrWhiteSpace(metaDescription) ? null : metaDescription.Trim();
    }
}
