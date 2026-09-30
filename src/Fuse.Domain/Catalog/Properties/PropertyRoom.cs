using Fuse.Domain.Catalog.Images;

namespace Fuse.Domain.Catalog.Properties;

public class PropertyRoom : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private PropertyRoom() { }

    public DefaultIdType PropertyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Description { get; private set; }
    public string? ShortDescription { get; private set; }
    public string? ImagePath { get; private set; }
    public string? PageTitle { get; private set; }
    public string? PageSubTitle { get; private set; }

    public virtual IList<Image>? Images { get; set; }

    public PropertyRoom(string name, string description, string? shortDescription, string? imagePath)
    {
        Name = name;
        Description = description;
        ShortDescription = shortDescription;
        ImagePath = imagePath != null ? !imagePath.Contains("w-1000") ? $"{imagePath}?w=1000" : imagePath : null;
    }

    public PropertyRoom Update(string? name, string? description, string? shortDescription, string? imagePath)
    {
        if (name is not null && Name?.Equals(name) is not true) Name = name;
        if (description is not null && Description?.Equals(description) is not true) Description = description;
        if (shortDescription is not null && ShortDescription?.Equals(shortDescription) is not true) ShortDescription = shortDescription;
        if (imagePath is not null && ImagePath?.Equals(imagePath) is not true) ImagePath = !imagePath.Contains("w-1000") ? $"{imagePath}?w=1000" : imagePath;
        return this;
    }

    public static PropertyRoom Create(DefaultIdType propertyId, string name, string? shortDescription, string description) =>
        new() { PropertyId = propertyId, Name = name.Trim(), ShortDescription = shortDescription.NormalizeRichText(), Description = description.NormalizeRichText() ?? string.Empty };

    public void Apply(string name, string? shortDescription, string description)
    {
        Name = name.Trim();
        ShortDescription = shortDescription.NormalizeRichText();
        Description = description.NormalizeRichText() ?? string.Empty;
    }

    public void SetImage(string? url) => ImagePath = Common.ImageUrls.Sized(url, Common.ImageUrls.Full);

    public PropertyRoom ClearImagePath()
    {
        ImagePath = string.Empty;
        return this;
    }
}