namespace Fuse.Domain.Catalog.Tours;

/// <summary>
/// A departure's manifest having been emailed to the hostels (legacy's scheduled "Today's Tour Manifests"): one per
/// tour and start time, so it's sent once however often the job runs, restarts, or how many servers run it.
/// No longer written: the manifest job now schedules each departure once at midnight, as legacy. The table stays
/// until a migration drops it.
/// </summary>
public class TourManifestEmail : BaseEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private TourManifestEmail() { }

    public TourManifestEmail(DefaultIdType tourId, DateTime startDate, int guests, IEnumerable<string> recipients, DateTime sentOnUtc)
    {
        TourId = tourId;
        StartDate = startDate;
        Guests = guests;
        Recipients = string.Join(", ", recipients);
        SentOn = sentOnUtc;
    }

    public DefaultIdType TourId { get; private set; }

    /// <summary>The departure's start (Vietnam time, as TourDates).</summary>
    public DateTime StartDate { get; private set; }

    /// <summary>Guests on the manifest when it was sent.</summary>
    public int Guests { get; private set; }

    /// <summary>Who it went to; empty when the tour's locations have no hostel email.</summary>
    [MaxLength(2000)] public string Recipients { get; private set; } = "";

    /// <summary>UTC.</summary>
    public DateTime SentOn { get; private set; }
}
