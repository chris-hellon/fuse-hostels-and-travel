using Fuse.Domain.Common;

namespace Fuse.Domain.Catalog.Tours;

public class TourImage : AuditableEntity, IAggregateRoot
{
    private TourImage() { }

    public static TourImage Create(DefaultIdType tourId, string url, int sortOrder) => new()
    {
        TourId = tourId,
        ImagePath = ImageUrls.Sized(url, ImageUrls.Full)!,
        ThumbnailImagePath = ImageUrls.Sized(url, ImageUrls.Thumbnail)!,
        SortOrder = sortOrder,
    };

    public DefaultIdType TourId { get; private set; }
    public string ImagePath { get; private set; } = null!;
    public string ThumbnailImagePath { get; private set; } = null!;
    public int? SortOrder { get; set; }
}
