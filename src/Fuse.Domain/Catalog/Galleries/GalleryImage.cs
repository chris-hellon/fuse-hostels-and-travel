namespace Fuse.Domain.Catalog.Galleries;

public class GalleryImage : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private GalleryImage() { }

    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? ImagePath { get; private set; }
    public int SortOrder { get; private set; }
    public DefaultIdType GalleryId { get; private set; }

    public GalleryImage(string title, string? description, string? imagePath, int sortOrder)
    {
        Title = title;
        Description = description;
        ImagePath = imagePath;
        SortOrder = sortOrder;
    }
    
    public GalleryImage(string title, string? description, string? imagePath, int sortOrder, DefaultIdType galleryId)
    {
        Title = title;
        Description = description;
        ImagePath = imagePath;
        SortOrder = sortOrder;
        GalleryId = galleryId;
    }
    
    public GalleryImage Update(string? title, string? description, string? imagePath, int? sortOrder, DefaultIdType galleryId)
    {
        if (title is not null && Title != title)
            Title = title;

        if (description is not null && Description != description)
            Description = description;

        if (imagePath is not null && ImagePath != imagePath)
            ImagePath = imagePath;

        if (sortOrder.HasValue && SortOrder != sortOrder)
            SortOrder = sortOrder.Value;

        GalleryId = galleryId;
        
        return this;
    }
    
    public GalleryImage Update(string? title, string? description, string? imagePath, int? sortOrder)
    {
        if (title is not null && Title != title)
            Title = title;

        if (description is not null && Description != description)
            Description = description;

        if (imagePath is not null && ImagePath != imagePath)
            ImagePath = imagePath;

        if (sortOrder.HasValue && SortOrder != sortOrder)
            SortOrder = sortOrder.Value;

        return this;
    }

    public GalleryImage ClearImagePath()
    {
        ImagePath = string.Empty;
        return this;
    }

    /// <summary>As legacy, an image is titled after its gallery.</summary>
    public static GalleryImage Create(DefaultIdType galleryId, string galleryTitle, string imagePath, int sortOrder) =>
        new(galleryTitle, galleryTitle, imagePath, sortOrder, galleryId);

    public void MoveTo(int sortOrder) => SortOrder = sortOrder;
}
