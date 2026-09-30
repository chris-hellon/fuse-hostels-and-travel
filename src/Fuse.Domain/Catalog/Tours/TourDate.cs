namespace Fuse.Domain.Catalog.Tours;

/// <summary>
/// A departure of a tour on one price option. <see cref="AvailableSpaces"/> is a counter bookings
/// draw down; departures sharing tour, start and end ("siblings", one per price) share capacity.
/// </summary>
public class TourDate : AuditableEntity, IAggregateRoot
{
    private TourDate() { }

    public static TourDate Create(DefaultIdType tourId, TourPrice price, DateTime start, int capacity, decimal? priceOverride = null) => new()
    {
        TourId = tourId,
        TourPriceId = price.Id,
        StartDate = start,
        EndDate = price.EndFor(start),
        AvailableSpaces = capacity,
        PriceOverride = priceOverride,
    };

    public DateTime StartDate { get; set; }
    public DefaultIdType TourId { get; set; }
    public DateTime EndDate { get; set; }
    public int AvailableSpaces { get; set; }
    public decimal? PriceOverride { get; set; }
    public DefaultIdType? TourPriceId { get; set; }
    public int ConcurrencyVersion { get; set; }
    public virtual TourPrice TourPrice { get; private set; } = null!;

    public void Reschedule(DateTime start, TourPrice price, decimal? priceOverride)
    {
        StartDate = start;
        TourPriceId = price.Id;
        EndDate = price.EndFor(start);
        PriceOverride = priceOverride;
    }

    /// <summary>Capacity changed on the tour: move every open departure by the same amount, never below zero.</summary>
    public void AdjustCapacity(int delta) => AvailableSpaces = Math.Max(0, AvailableSpaces + delta);

    public void DetachTourPrice() => TourPrice = null!;
}
