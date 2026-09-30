using Fuse.Domain.Catalog.Tours;

namespace Fuse.Domain.Catalog.Packages;

/// <summary>A package's editable fields (tiers, tours and prices are reconciled separately).</summary>
public sealed record PackageDetails
{
    public required string Name { get; init; }
    public DefaultIdType? TourCategoryId { get; init; }
    public string? PriceLabel { get; init; }
    public decimal? CommissionAmount { get; init; }
    public bool CommissionIgnoresMinimumPax { get; init; }
    public string? Notes { get; init; }
    public string? ShortDescription { get; init; }
    public string? Description { get; init; }
    public bool PublishToSite { get; init; }
}

public class Package : AuditableEntity, IAggregateRoot
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? ShortDescription { get; private set; }
    public string? ImagePath { get; private set; }
    public string? ThumbnailImagePath { get; private set; }
    public string? PriceLabel { get; private set; }
    public bool? PublishToSite { get; private set; }
    public DefaultIdType? TourCategoryId { get; private set; }

    /// <summary>
    /// Flat staff commission per guest for selling the whole package, paid instead of its tours'
    /// individual commissions. If some of its tours are cancelled, the share for the tours that ran is
    /// paid (a $2 combo with one of two tours cancelled pays $1). Null means each tour earns its own
    /// commission under its own rules.
    /// </summary>
    public decimal? CommissionAmount { get; private set; }

    /// <summary>When true, <see cref="CommissionAmount"/> is paid even if its tours miss their minimum pax.</summary>
    public bool? CommissionIgnoresMinimumPax { get; private set; }

    /// <summary>Internal free-text notes about the package (staff-only, not shown to guests).</summary>
    public string? Notes { get; private set; }

    public virtual TourCategory? TourCategory { get; set; }
    public virtual IList<PackageTier>? PackageTiers { get; set; }
    public virtual IList<PackageItem>? PackageItems { get; set; }

    public Package()
    {
    }

    public Package(string name,
        string? description,
        string? shortDescription,
        string? imagePath,
        string? thumbnailImagePath,
        string? priceLabel,
        bool? publishToSite,
        DefaultIdType? tourCategoryId,
        decimal? commissionAmount,
        bool? commissionIgnoresMinimumPax,
        string? notes = null)
    {
        Name = name;
        Description = description.NormalizeRichText();
        ShortDescription = shortDescription.NormalizeRichText();
        ImagePath = string.IsNullOrWhiteSpace(imagePath) ? null : (!imagePath.Contains("w-1000") ? $"{imagePath}?w=1000" : imagePath);
        ThumbnailImagePath = string.IsNullOrWhiteSpace(thumbnailImagePath) ? null : (!thumbnailImagePath.Contains("w-700") ? $"{thumbnailImagePath}?w=700" : thumbnailImagePath);
        PriceLabel = priceLabel;
        PublishToSite = publishToSite;
        TourCategoryId = tourCategoryId;
        CommissionAmount = commissionAmount;
        CommissionIgnoresMinimumPax = commissionIgnoresMinimumPax;
        Notes = notes;
    }

    public Package Update(string? name,
        string? description,
        string? shortDescription,
        string? imagePath,
        string? thumbnailImagePath,
        string? priceLabel,
        bool? publishToSite,
        DefaultIdType? tourCategoryId,
        decimal? commissionAmount,
        bool? commissionIgnoresMinimumPax,
        string? notes = null)
    {
        if (name is not null && Name?.Equals(name) is not true) Name = name;
        if (description is not null && Description?.Equals(description) is not true) Description = description.NormalizeRichText();
        if (shortDescription is not null && ShortDescription?.Equals(shortDescription) is not true) ShortDescription = shortDescription.NormalizeRichText();
        if (!string.IsNullOrWhiteSpace(imagePath) && ImagePath?.Equals(imagePath) is not true) ImagePath = !imagePath.Contains("w-1000") ? $"{imagePath}?w=1000" : imagePath;
        if (!string.IsNullOrWhiteSpace(thumbnailImagePath) && ThumbnailImagePath?.Equals(thumbnailImagePath) is not true) ThumbnailImagePath = !thumbnailImagePath.Contains("w-700") ? $"{thumbnailImagePath}?w=700" : thumbnailImagePath;
        if (priceLabel is not null && PriceLabel?.Equals(priceLabel) is not true) PriceLabel = priceLabel;

        PublishToSite = publishToSite;
        TourCategoryId = tourCategoryId;
        CommissionAmount = commissionAmount;
        CommissionIgnoresMinimumPax = commissionIgnoresMinimumPax;
        Notes = notes;

        return this;
    }

    public static Package Create(PackageDetails details)
    {
        var package = new Package();
        package.Apply(details);
        return package;
    }

    public void Apply(PackageDetails details)
    {
        Name = details.Name.Trim();
        TourCategoryId = details.TourCategoryId;
        PriceLabel = string.IsNullOrWhiteSpace(details.PriceLabel) ? null : details.PriceLabel.Trim();
        CommissionAmount = details.CommissionAmount;
        // Only means something alongside a package commission.
        CommissionIgnoresMinimumPax = details.CommissionAmount is not null && details.CommissionIgnoresMinimumPax;
        Notes = string.IsNullOrWhiteSpace(details.Notes) ? null : details.Notes;
        ShortDescription = details.ShortDescription.NormalizeRichText();
        Description = details.Description.NormalizeRichText();
        PublishToSite = details.PublishToSite;
    }

    public void SetImage(string? url)
    {
        ImagePath = Common.ImageUrls.Sized(url, Common.ImageUrls.Full);
        ThumbnailImagePath = Common.ImageUrls.Sized(url, Common.ImageUrls.Thumbnail);
    }

    /// <summary>
    /// The package total for a given tier: the sum of each included tour's price for that tier × quantity.
    /// Per person, consistent with how tours are priced. Requires items and their prices to be loaded.
    /// </summary>
    public decimal TotalPriceForTier(DefaultIdType tierId) =>
        PackageItems?.Sum(i => i.PriceForTier(tierId) * i.Quantity) ?? 0m;

    public Package ClearImagePath()
    {
        ImagePath = string.Empty;
        ThumbnailImagePath = string.Empty;
        return this;
    }
}
