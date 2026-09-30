namespace Fuse.Domain.Catalog.Tours;

/// <summary>
/// What a tour price's commission minimum pax is measured against. The count is every paying guest
/// on the tour, not just the ones the staff member sold.
/// </summary>
public enum CommissionMinimumPaxScope
{
    /// <summary>
    /// The departure the guest is on (e.g. "min 10 pax on tour"): everyone on the tour at that start
    /// time, across every price option - standard and combo prices are separate tour dates at the same time.
    /// </summary>
    PerDeparture = 0,

    /// <summary>All departures of the tour on that calendar day (e.g. "min 18 pax per day").</summary>
    PerDay = 1
}
