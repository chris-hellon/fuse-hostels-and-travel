namespace Fuse.Domain.Catalog.TravelGuides;

public class TravelGuideGalleryImage : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private TravelGuideGalleryImage() { }

    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? ImagePath { get; private set; }
    public int SortOrder { get; private set; }
    public DefaultIdType TravelGuideId { get; private set; }

    /// <summary>As legacy, a slideshow image is titled after its guide.</summary>
    public static TravelGuideGalleryImage Create(DefaultIdType travelGuideId, string guideTitle, string imagePath, int sortOrder) => new()
    {
        TravelGuideId = travelGuideId,
        Title = guideTitle,
        Description = guideTitle,
        ImagePath = imagePath,
        SortOrder = sortOrder,
    };

    public void MoveTo(int sortOrder) => SortOrder = sortOrder;
}