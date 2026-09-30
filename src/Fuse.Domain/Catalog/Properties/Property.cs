using Fuse.Domain.Catalog.Events;
using Fuse.Domain.Catalog.Tours;

namespace Fuse.Domain.Catalog.Properties;

/// <summary>A property's editable fields (rooms, directions and media are reconciled separately).</summary>
public sealed record PropertyDetails
{
    public required string Name { get; init; }
    public string? EmailAddress { get; init; }
    public bool PublishToSite { get; init; }
    public required string AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public required string TelephoneNumber { get; init; }
    public string? GoogleMapsPlaceId { get; init; }
    public string? ShortDescription { get; init; }
    public required string Description { get; init; }
    public string? SeoPageTitle { get; init; }
    public string? H1 { get; init; }
    public string? H2 { get; init; }
    public string? UrlSlug { get; init; }
    public string? MetaKeywords { get; init; }
    public string? MetaDescription { get; init; }
    public string? CustomSeoScripts { get; init; }
}

public class Property : AuditableEntity, IAggregateRoot
{
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public string? ShortDescription { get; private set; }
    public string? ImagePath { get; private set; }
    public string? ThumbnailImagePath { get; private set; }
    public string AddressLine1 { get; private set; } = null!;
    public string? AddressLine2 { get; private set; }
    public string TelephoneNumber { get; private set; } = null!;
    public string? EmailAddress { get; private set; }
    public string? GoogleMapsPlaceId { get; private set; }
    public string? PageTitle { get; private set; }
    public string? PageSubTitle { get; private set; }
    public string? CloudbedsKey { get; set; }
    public string? CloudbedsApiKey { get; set; }
    public string? CloudbedsPropertyId { get; set; }
    public string? MetaKeywords { get; private set; }
    public string? MetaDescription { get; private set; }
    public bool? PublishToSite { get; private set; }
    public string? UrlSlug { get; private set; }
    public string? H1 { get; private set; }
    public string? H2 { get; private set; }
    public string? VideoPath { get; private set; }
    public string? MobileVideoPath { get; private set; }
    public string? CustomSeoScripts { get; private set; }
    public string? SeoPageTitle { get; private set; }
    
    public virtual IList<TourPropertyLookup> TourPropertyLookups { get; set; } = null!;
    public virtual IList<PropertyDestinationLookup> PropertyDestinationLookups { get; set; } = null!;
    public virtual IList<PropertyDirection> Directions { get; set; } = null!;
    public virtual IList<PropertyRoom> Rooms { get; set; } = null!;
    public virtual IList<PropertyFacility> Facilities { get; set; } = null!;
    public virtual IList<Event> Events { get; set; } = null!;
    public virtual IList<PropertyImage>? Images { get; set; }

    public Property()
    {

    }

    public Property(string name,
        string description,
        string? shortDescription,
        string? imagePath,
        string? thumbnailImagePath,
        string addressLine1,
        string? addressLine2,
        string telephoneNumber,
        string? googleMapsPlaceId,
        string? pageTitle,
        string? pageSubTitle,
        string? cloudbedsKey,
        string? metaKeywords,
        string? metaDescription,
        string? emailAddress,
        bool? publishToSite,
        string? urlSlug,
        string? h1,
        string? h2,
        string? videoPath,
        string? mobileVideoPath,
        string? cloudbedsApiKey,
        string? cloudbedsPropertyId, 
        string? customSeoScripts,
        string? seoPageTitle)
    {
        Name = name;
        Description = description.NormalizeRichText() ?? string.Empty;
        ShortDescription = shortDescription.NormalizeRichText() ?? string.Empty;
        ImagePath = imagePath != null ? !imagePath.Contains("w-1000") ? $"{imagePath}?w=1000" : imagePath : null;
        ThumbnailImagePath = thumbnailImagePath != null ? !thumbnailImagePath.Contains("w-700") ? $"{thumbnailImagePath}?w=700" : thumbnailImagePath : null;
        AddressLine1 = addressLine1;
        AddressLine2 = addressLine2;
        TelephoneNumber = telephoneNumber;
        GoogleMapsPlaceId = googleMapsPlaceId;
        PageTitle = pageTitle;
        PageSubTitle = pageSubTitle;
        CloudbedsKey = cloudbedsKey;
        CloudbedsApiKey = cloudbedsApiKey;
        CloudbedsPropertyId = cloudbedsPropertyId;
        MetaKeywords = metaKeywords;
        MetaDescription = metaDescription;
        EmailAddress = emailAddress;
        PublishToSite = publishToSite;
        UrlSlug = urlSlug;
        H1 = h1;
        H2 = h2;
        VideoPath = videoPath;
        MobileVideoPath = mobileVideoPath;
        CustomSeoScripts = customSeoScripts;
        SeoPageTitle = seoPageTitle;
    }

    public Property Update(string? name,
        string? description,
        string? shortDescription,
        string? imagePath,
        string? thumbnailImagePath,
        string? addressLine1,
        string? addressLine2,
        string? telephoneNumber,
        string? googleMapsPlaceId,
        string? pageTitle,
        string? pageSubTitle,
        string? cloudbedsKey,
        string? metaKeywords,
        string? metaDescription,
        string? emailAddress,
        bool? publishToSite,
        string? urlSlug,
        string? h1,
        string? h2,
        string? videoPath,
        string? mobileVideoPath,
        string? cloudbedsApiKey,
        string? cloudbedsPropertyId, 
        string? customSeoScripts,
        string? seoPageTitle)
    {
        if (name is not null && Name?.Equals(name) is not true) Name = name;
        if (description is not null && Description?.Equals(description) is not true) Description = description.NormalizeRichText() ?? string.Empty;
        if (shortDescription is not null && ShortDescription?.Equals(shortDescription) is not true) ShortDescription = shortDescription.NormalizeRichText();
        if (imagePath is not null && ImagePath?.Equals(imagePath) is not true) ImagePath = !imagePath.Contains("w-1000") ? $"{imagePath}?w=1000" : imagePath;
        if (thumbnailImagePath is not null && ThumbnailImagePath?.Equals(thumbnailImagePath) is not true) ThumbnailImagePath = thumbnailImagePath.Contains("w-700") ? $"{thumbnailImagePath}?w=700" : thumbnailImagePath;
        if (addressLine1 is not null && AddressLine1?.Equals(addressLine1) is not true) AddressLine1 = addressLine1;
        if (addressLine2 is not null && AddressLine2?.Equals(addressLine2) is not true) AddressLine2 = addressLine2;
        if (addressLine1 is not null && AddressLine1?.Equals(addressLine1) is not true) AddressLine1 = addressLine1;
        if (telephoneNumber is not null && TelephoneNumber?.Equals(telephoneNumber) is not true) TelephoneNumber = telephoneNumber;
        if (googleMapsPlaceId is not null && GoogleMapsPlaceId?.Equals(googleMapsPlaceId) is not true) GoogleMapsPlaceId = googleMapsPlaceId;
        if (pageTitle is not null && PageTitle?.Equals(pageTitle) is not true) PageTitle = pageTitle;
        if (pageSubTitle is not null && PageSubTitle?.Equals(pageSubTitle) is not true) PageSubTitle = pageSubTitle;
        if (cloudbedsKey is not null && CloudbedsKey?.Equals(cloudbedsKey) is not true) CloudbedsKey = cloudbedsKey;
        if (cloudbedsApiKey is not null && CloudbedsApiKey?.Equals(cloudbedsApiKey) is not true) CloudbedsApiKey = cloudbedsApiKey;
        if (cloudbedsPropertyId is not null && CloudbedsPropertyId?.Equals(cloudbedsPropertyId) is not true) CloudbedsPropertyId = cloudbedsPropertyId;
        if (metaKeywords is not null && MetaKeywords?.Equals(metaKeywords) is not true) MetaKeywords = metaKeywords;
        if (metaDescription is not null && MetaDescription?.Equals(metaDescription) is not true) MetaDescription = metaDescription;
        if (emailAddress is not null && EmailAddress?.Equals(emailAddress) is not true) EmailAddress = emailAddress;
        if (urlSlug is not null && UrlSlug?.Equals(urlSlug) is not true) UrlSlug = urlSlug;
        if (h1 is not null && H1?.Equals(h1) is not true) H1 = h1;
        if (h2 is not null && H2?.Equals(h2) is not true) H2 = h2;
        if (videoPath is not null && VideoPath?.Equals(videoPath) is not true) VideoPath = videoPath;
        if (mobileVideoPath is not null && MobileVideoPath?.Equals(mobileVideoPath) is not true) MobileVideoPath = mobileVideoPath;
        if (customSeoScripts is not null && CustomSeoScripts?.Equals(customSeoScripts) is not true) CustomSeoScripts = customSeoScripts;
        if (seoPageTitle is not null && SeoPageTitle?.Equals(seoPageTitle) is not true) SeoPageTitle = seoPageTitle;
        
        PublishToSite = publishToSite;

        return this;
    }


    public static Property Create(PropertyDetails details)
    {
        var property = new Property();
        property.Apply(details);
        return property;
    }

    public void Apply(PropertyDetails details)
    {
        Name = details.Name.Trim();
        EmailAddress = Blank(details.EmailAddress);
        PublishToSite = details.PublishToSite;
        AddressLine1 = details.AddressLine1.Trim();
        AddressLine2 = Blank(details.AddressLine2);
        TelephoneNumber = details.TelephoneNumber.Trim();
        GoogleMapsPlaceId = Blank(details.GoogleMapsPlaceId);
        ShortDescription = details.ShortDescription.NormalizeRichText();
        Description = details.Description.NormalizeRichText() ?? string.Empty;
        SeoPageTitle = Blank(details.SeoPageTitle);
        H1 = Blank(details.H1);
        H2 = Blank(details.H2);
        UrlSlug = Blank(details.UrlSlug);
        MetaKeywords = Blank(details.MetaKeywords);
        MetaDescription = Blank(details.MetaDescription);
        CustomSeoScripts = Blank(details.CustomSeoScripts);
    }

    public void SetBanner(string? url)
    {
        ImagePath = Common.ImageUrls.Sized(url, Common.ImageUrls.Full);
        ThumbnailImagePath = Common.ImageUrls.Sized(url, Common.ImageUrls.Thumbnail);
    }

    public void SetVideo(string? url) => VideoPath = url;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public Property ClearImagePath()
    {
        ImagePath = string.Empty;
        ThumbnailImagePath = string.Empty;
        return this;
    }

    public Property ClearVideoPath()
    {
        VideoPath = string.Empty;
        return this;
    }

    public Property ClearMobileVideoPath()
    {
        MobileVideoPath = string.Empty;
        return this;
    }
}