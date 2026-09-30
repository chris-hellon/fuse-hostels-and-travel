using Fuse.Domain.Common;

namespace Fuse.Domain.Catalog.Tours;

/// <summary>Everything an editor sets on a tour's details (not prices, dates or media).</summary>
public sealed record TourDetails
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string? ShortDescription { get; init; }
    public string? WhatsIncluded { get; init; }
    public string? WhatsNotIncluded { get; init; }
    public string? AdditionalInformation { get; init; }
    public string? ImportantInformation { get; init; }
    public string? TelephoneNumber { get; init; }
    public string? DayDuration { get; init; }
    public string? NightDuration { get; init; }
    public string? HourDuration { get; init; }
    public int? MinCapacity { get; init; }
    /// <summary>Null = unlimited.</summary>
    public int? MaxCapacity { get; init; }
    public bool PublishToSite { get; init; }
    public DefaultIdType? TourCategoryId { get; init; }
    public string? SupplierId { get; init; }
    public bool WaiverRequired { get; init; }
    public bool ShowBookingQRCode { get; init; }
    public bool ExcludeFromStaffSales { get; init; }
    public bool AdditionalGuestDetailsRequired { get; init; }
    public string? BookingConfirmationEmailDetails { get; init; }
    public string? TermsAndConditions { get; init; }
    public string? CancellationPolicy { get; init; }
    public string? SeoPageTitle { get; init; }
    public string? H1 { get; init; }
    public string? H2 { get; init; }
    public string? UrlSlug { get; init; }
    public string? MetaKeywords { get; init; }
    public string? MetaDescription { get; init; }
    public string? CustomSeoScripts { get; init; }
}

public class Tour : AuditableEntity, IAggregateRoot
{
    /// <summary>Stored for "unlimited" capacity: date seat counters and legacy reports rely on a number.</summary>
    public const int UnlimitedCapacity = 99999;

    private Tour() { }

    public static Tour Create(TourDetails details)
    {
        var tour = new Tour();
        tour.Apply(details);
        return tour;
    }

    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public string? ShortDescription { get; private set; }
    public decimal? Price { get; private set; }
    public string? PriceLabel { get; private set; }
    public string? ImagePath { get; private set; }
    public string? ThumbnailImagePath { get; private set; }
    public int? MaxCapacity { get; private set; }
    public int? MinCapacity { get; private set; }
    public string? DayDuration { get; private set; }
    public string? NightDuration { get; private set; }
    public string? HourDuration { get; private set; }
    public string? Address { get; private set; }
    public string? TelephoneNumber { get; private set; }
    public string? WhatsIncluded { get; private set; }
    public string? WhatsNotIncluded { get; private set; }
    public string? AdditionalInformation { get; private set; }
    public string? ImportantInformation { get; private set; }
    public string? MetaKeywords { get; private set; }
    public string? MetaDescription { get; private set; }
    public bool? PublishToSite { get; private set; }
    public string? UrlSlug { get; private set; }
    public string? H1 { get; private set; }
    public string? H2 { get; private set; }
    public string? VideoPath { get; private set; }
    public string? MobileVideoPath { get; private set; }
    public string? BookingConfirmationEmailDetails { get; private set; }
    public string? TermsAndConditions { get; private set; }
    public string? CancellationPolicy { get; private set; }
    public bool? AdditionalGuestDetailsRequired { get; private set; }
    public bool? WaiverRequired { get; private set; }
    /// <summary>The supplier's user id. This alone decides which tours a supplier sees (no claims).</summary>
    public string? SupplierId { get; private set; }
    public string? SupplierEmailText { get; private set; }
    public bool? ShowBookingQRCode { get; private set; }
    public bool? ExcludeFromStaffSales { get; private set; }
    public DefaultIdType? TourCategoryId { get; private set; }
    public string? CustomSeoScripts { get; private set; }
    public string? SeoPageTitle { get; private set; }

    public virtual IList<TourCategoryLookup>? TourCategoryLookups { get; set; }
    public virtual IList<TourPropertyLookup>? TourPropertyLookups { get; private set; }
    public virtual IList<TourDestinationLookup>? TourDestinationLookups { get; set; }
    public virtual IList<TourPrice>? TourPrices { get; set; }
    public virtual IList<TourDate>? TourDates { get; set; }
    public virtual IList<TourItinerary>? TourItineraries { get; set; }
    public virtual IList<TourImage>? Images { get; set; }
    public virtual IList<TourPickupLocation>? TourPickupLocations { get; set; }
    public virtual TourCategory? TourCategory { get; set; }

    /// <summary>Capacity as a number (unlimited = <see cref="UnlimitedCapacity"/>).</summary>
    public int Capacity => MaxCapacity ?? UnlimitedCapacity;

    /// <summary>
    /// Applies the editable details. Returns how much each date's seat count must change because
    /// capacity changed (legacy computed this but never applied it).
    /// </summary>
    public int Apply(TourDetails details)
    {
        var previousCapacity = Capacity;

        Name = details.Name.Trim();
        Description = details.Description.NormalizeRichText() ?? "";
        ShortDescription = details.ShortDescription.NormalizeRichText();
        WhatsIncluded = details.WhatsIncluded.NormalizeRichText();
        WhatsNotIncluded = details.WhatsNotIncluded.NormalizeRichText();
        AdditionalInformation = details.AdditionalInformation.NormalizeRichText();
        ImportantInformation = details.ImportantInformation.NormalizeRichText();
        // Address isn't edited here (legacy's form never showed it, and its Update kept it); the public API returns it.
        TelephoneNumber = details.TelephoneNumber;
        DayDuration = details.DayDuration;
        NightDuration = details.NightDuration;
        HourDuration = details.HourDuration;
        MinCapacity = details.MinCapacity;
        MaxCapacity = details.MaxCapacity ?? UnlimitedCapacity;
        PublishToSite = details.PublishToSite;
        TourCategoryId = details.TourCategoryId;
        SupplierId = string.IsNullOrWhiteSpace(details.SupplierId) ? null : details.SupplierId;
        WaiverRequired = details.WaiverRequired;
        ShowBookingQRCode = details.ShowBookingQRCode;
        ExcludeFromStaffSales = details.ExcludeFromStaffSales;
        AdditionalGuestDetailsRequired = details.AdditionalGuestDetailsRequired;
        BookingConfirmationEmailDetails = details.BookingConfirmationEmailDetails.NormalizeRichText();
        TermsAndConditions = details.TermsAndConditions.NormalizeRichText();
        CancellationPolicy = details.CancellationPolicy.NormalizeRichText();
        SeoPageTitle = details.SeoPageTitle;
        H1 = string.IsNullOrWhiteSpace(details.H1) ? Name : details.H1;
        H2 = details.H2;
        UrlSlug = details.UrlSlug;
        MetaKeywords = details.MetaKeywords;
        MetaDescription = details.MetaDescription;
        CustomSeoScripts = details.CustomSeoScripts;

        return Capacity - previousCapacity;
    }

    public void SetBanner(string? url)
    {
        ImagePath = ImageUrls.Sized(url, ImageUrls.Full) ?? "";
        ThumbnailImagePath = ImageUrls.Sized(url, ImageUrls.Thumbnail) ?? "";
    }

    public void SetVideo(string? url) => VideoPath = url ?? "";

    public void SetMobileVideo(string? url) => MobileVideoPath = url ?? "";
}
