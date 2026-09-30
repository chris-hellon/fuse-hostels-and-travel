namespace Fuse.Domain.Catalog.Bookings;

/// <summary>
/// Records that a <see cref="Booking"/> included a package. Acts both as the grouping row for the
/// expanded per-tour <see cref="BookingItem"/>s and as a point-in-time snapshot of the package, so
/// historical bookings stay correct even if the catalog package is later edited or deleted.
/// </summary>
public class BookingPackage : AuditableEntity, IAggregateRoot
{
    public DefaultIdType BookingId { get; private set; } = DefaultIdType.Empty;

    /// <summary>The catalog package (template) this was created from.</summary>
    public DefaultIdType PackageId { get; private set; }

    /// <summary>Snapshot of the package name at time of booking.</summary>
    public string PackageName { get; private set; } = null!;

    /// <summary>The package total charged for the chosen tier (per person), snapshotted at booking time.</summary>
    public decimal PackagePrice { get; private set; }

    /// <summary>Snapshot of the chosen tier's title (e.g. "Adult"), if the package was booked at a specific tier.</summary>
    public string? TierTitle { get; private set; }

    /// <summary>Snapshot of the package's staff commission per guest; null when its tours earn their own.</summary>
    public decimal? CommissionAmount { get; private set; }

    /// <summary>Snapshot of whether the package commission is paid regardless of the tours' minimum pax.</summary>
    public bool? CommissionIgnoresMinimumPax { get; private set; }

    public virtual Booking Booking { get; private set; } = null!;
    public virtual IList<BookingItem>? Items { get; set; }

    public BookingPackage()
    {
    }

    public BookingPackage(DefaultIdType packageId, string packageName, decimal packagePrice, string? tierTitle,
        decimal? commissionAmount, bool? commissionIgnoresMinimumPax, DefaultIdType createdBy, DefaultIdType? bookingId = null)
    {
        PackageId = packageId;
        PackageName = packageName;
        PackagePrice = packagePrice;
        TierTitle = tierTitle;
        CommissionAmount = commissionAmount;
        CommissionIgnoresMinimumPax = commissionIgnoresMinimumPax;
        CreatedBy = createdBy;

        if (bookingId.HasValue)
        {
            BookingId = bookingId.Value;
        }
    }

    /// <summary>Refresh the snapshot when the package is re-configured on an existing booking (e.g. a tier change).</summary>
    public BookingPackage Update(string packageName, decimal packagePrice, string? tierTitle,
        decimal? commissionAmount, bool? commissionIgnoresMinimumPax)
    {
        PackageName = packageName;
        PackagePrice = packagePrice;
        TierTitle = tierTitle;
        CommissionAmount = commissionAmount;
        CommissionIgnoresMinimumPax = commissionIgnoresMinimumPax;

        return this;
    }
}
