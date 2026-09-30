namespace Fuse.Domain.Catalog.Commission;

/// <summary>
/// A staff-sales "pack" incentive: a flat commission per guest paid when a booking is marked as this pack
/// and contains at least <see cref="MinimumProductCount"/> distinct eligible tours that operated. Managed
/// in the system (amount, threshold and eligible tours are all configurable) — nothing is hard-coded.
/// e.g. "Pack 3" = any 3 of {BT, FT, RC, SUP} → $6/guest; "Pack 4" = all 4 → $10/guest.
/// </summary>
public class PackIncentive : AuditableEntity, IAggregateRoot
{
    public string Name { get; private set; } = null!;

    /// <summary>How many distinct eligible tours a booking must contain to qualify (a minimum).</summary>
    public int MinimumProductCount { get; private set; }

    /// <summary>The flat commission paid per guest when the pack qualifies.</summary>
    public decimal CommissionAmount { get; private set; }

    /// <summary>When true, the pack pays even if its tours miss their own minimum pax (still requires the tour to operate).</summary>
    public bool IgnoresMinimumPax { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>Internal free-text notes about the pack (staff-only).</summary>
    public string? Notes { get; private set; }

    /// <summary>The tours that count toward this pack.</summary>
    public virtual IList<PackIncentiveTour>? Tours { get; set; }

    /// <summary>The pack's sale-price tiers (e.g. Adult / Child), each with a total price.</summary>
    public virtual IList<PackIncentiveTier>? Tiers { get; set; }

    public PackIncentive()
    {
    }

    public PackIncentive(string name, int minimumProductCount, decimal commissionAmount, bool ignoresMinimumPax, bool isActive, string? notes = null)
    {
        Name = name;
        MinimumProductCount = minimumProductCount < 1 ? 1 : minimumProductCount;
        CommissionAmount = commissionAmount;
        IgnoresMinimumPax = ignoresMinimumPax;
        IsActive = isActive;
        Notes = notes;
    }

    public PackIncentive Update(string? name, int? minimumProductCount, decimal? commissionAmount, bool? ignoresMinimumPax, bool? isActive, string? notes = null)
    {
        if (name is not null && Name != name) Name = name;
        if (minimumProductCount is >= 1 && MinimumProductCount != minimumProductCount) MinimumProductCount = minimumProductCount.Value;
        if (commissionAmount.HasValue && CommissionAmount != commissionAmount) CommissionAmount = commissionAmount.Value;
        if (ignoresMinimumPax.HasValue) IgnoresMinimumPax = ignoresMinimumPax.Value;
        if (isActive.HasValue) IsActive = isActive.Value;
        Notes = notes;

        return this;
    }
}
