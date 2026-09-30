using Fuse.Domain.Catalog.Tours;

namespace Fuse.Domain.Catalog.Commission;

/// <summary>A tour that counts toward a <see cref="PackIncentive"/>'s eligible-product set.</summary>
public class PackIncentiveTour : AuditableEntity, IAggregateRoot
{
    public DefaultIdType PackIncentiveId { get; set; }
    public DefaultIdType TourId { get; set; }

    public virtual Tour? Tour { get; set; }

    public PackIncentiveTour()
    {
    }

    public PackIncentiveTour(DefaultIdType tourId, DefaultIdType? packIncentiveId = null)
    {
        TourId = tourId;

        if (packIncentiveId.HasValue)
        {
            PackIncentiveId = packIncentiveId.Value;
        }
    }
}
