using Fuse.Domain.Catalog.Images;

namespace Fuse.Domain.Catalog.Destinations;

public class Destination : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private Destination() { }

    public Destination(string name,
        string description,
        string? shortDescription,
        string? directions,
        string? imagePath,
        string? thumbnailImagePath,
        string? googleMapsKey)
    {
        Name = name;
        Description = description;
        ShortDescription = shortDescription;
        Directions = directions;
        ImagePath = imagePath != null ? !imagePath.Contains("w-1000") ? $"{imagePath}?w=1000" : imagePath : null;
        ThumbnailImagePath = thumbnailImagePath != null ? !thumbnailImagePath.Contains("w-700") ? $"{thumbnailImagePath}?w=700" : thumbnailImagePath : null;
        GoogleMapsKey = googleMapsKey;
    }

    public string Name { get; private set; }
    public string Description { get; private set; }
    public string? ShortDescription { get; private set; }
    public string? Directions { get; private set; }
    public string? ImagePath { get; private set; }
    public string? ThumbnailImagePath { get; private set; }
    public string? GoogleMapsKey { get; private set; }
    public virtual IList<Image>? Images { get; set; }

    public Destination Update(string? name, string? description, string? shortDescription, string? directions, string? imagePath, string? thumbnailImagePath, string? googleMapsKey)
    {
        if (name is not null && Name?.Equals(name) is not true) Name = name;
        if (description is not null && Description?.Equals(description) is not true) Description = description;
        if (shortDescription is not null && ShortDescription?.Equals(shortDescription) is not true) ShortDescription = shortDescription;
        if (directions is not null && Directions?.Equals(directions) is not true) Directions = directions;
        if (imagePath is not null && ImagePath?.Equals(imagePath) is not true) ImagePath = !imagePath.Contains("w-1000") ? $"{imagePath}?w=1000" : imagePath;
        if (thumbnailImagePath is not null && ThumbnailImagePath?.Equals(thumbnailImagePath) is not true) ThumbnailImagePath = thumbnailImagePath.Contains("w-700") ? $"{thumbnailImagePath}?w=700" : thumbnailImagePath;
        if (googleMapsKey is not null && GoogleMapsKey?.Equals(googleMapsKey) is not true) GoogleMapsKey = googleMapsKey;

        return this;
    }

    public static Destination Create(string name, string? shortDescription, string? description, string? directions, string? googleMapsKey)
    {
        var destination = new Destination(name, "", null, null, null, null, null);
        destination.Apply(name, shortDescription, description, directions, googleMapsKey);
        return destination;
    }

    public void Apply(string name, string? shortDescription, string? description, string? directions, string? googleMapsKey)
    {
        Name = name.Trim();
        ShortDescription = shortDescription.NormalizeRichText();
        Description = description.NormalizeRichText() ?? "";
        Directions = directions.NormalizeRichText();
        GoogleMapsKey = googleMapsKey;
    }

    public void SetImage(string? url)
    {
        ImagePath = Common.ImageUrls.Sized(url, Common.ImageUrls.Full) ?? "";
        ThumbnailImagePath = Common.ImageUrls.Sized(url, Common.ImageUrls.Thumbnail) ?? "";
    }

    public Destination ClearImagePath()
    {
        ImagePath = string.Empty;
        ThumbnailImagePath = string.Empty;
        return this;
    }
}