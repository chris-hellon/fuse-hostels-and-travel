using Fuse.Shared.Events;
using Fuse.Domain.Catalog.Properties;

namespace Fuse.Domain.Catalog.Events;

public class Event : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private Event() { }

    public string? Name { get; private set; }
    public string? Description { get; private set; }
    public string? ShortDescription { get; private set; }
    public string? ImagePath { get; private set; }
    public string? BackgroundColor { get; private set; }

    /// <summary>Legacy single-hostel link, superseded by <see cref="EventPropertyLookups"/>. Kept for back-compat.</summary>
    public DefaultIdType? PropertyId { get; private set; }

    public DefaultIdType? EventCategoryId { get; private set; }

    // ---- Schedule ----
    public DateTime StartDate { get; private set; }
    public DateTime? EndDate { get; private set; }
    public bool AllDay { get; private set; }

    // ---- Visibility ----
    public bool? PublishToSite { get; private set; }
    public bool? IsFeatured { get; private set; }

    /// <summary>When true the event applies to every hostel (chain-wide) and <see cref="EventPropertyLookups"/> is ignored.</summary>
    public bool? AllProperties { get; private set; }

    // ---- Recurrence rule (occurrences computed on the fly) ----
    public EventRecurrenceType RecurrenceType { get; private set; }

    /// <summary>Repeat every N periods of <see cref="RecurrenceType"/> (default 1).</summary>
    public int? RecurrenceInterval { get; private set; }

    /// <summary>Weekly recurrences only: a bitmask of days, bit (1 &lt;&lt; (int)DayOfWeek). Null/0 = the start day.</summary>
    public int? RecurrenceDaysOfWeek { get; private set; }

    /// <summary>When recurrence stops (inclusive). Null = open-ended (the expander still caps the window).</summary>
    public DateTime? RecurrenceEndDate { get; private set; }

    /// <summary>Optional external link for the event (a Facebook event, ticket page, etc.).</summary>
    public string? ExternalUrl { get; private set; }

    /// <summary>A tour the event is booked as (e.g. a bar crawl with a price): the site offers each date's departure.</summary>
    public DefaultIdType? TourId { get; private set; }

    /// <summary>Its dates are the tour's departures (its own start and repeat rule are then unused).</summary>
    public bool FollowsTourDates { get; private set; }

    public virtual Property Property { get; set; } = null!;
    public virtual EventCategory? EventCategory { get; set; }
    public virtual Tours.Tour? Tour { get; set; }
    public virtual IList<EventPropertyLookup> EventPropertyLookups { get; set; } = new List<EventPropertyLookup>();
    public virtual IList<EventOccurrenceException> OccurrenceExceptions { get; set; } = new List<EventOccurrenceException>();
    public virtual IList<EventTimeSlot> TimeSlots { get; set; } = new List<EventTimeSlot>();

    public static Event Create(EventDetails details)
    {
        var @event = new Event();
        @event.Apply(details);
        return @event;
    }

    /// <summary>
    /// Everything the admin edits except the image and the hostel/cancellation/time-slot lists (owned rows the
    /// handler reconciles). The colour arrives as hex and is stored as legacy "r, g, b" (the website
    /// writes it into <c>rgb(...)</c>).
    /// </summary>
    public void Apply(EventDetails details)
    {
        Name = details.Name.Trim();
        EventCategoryId = details.EventCategoryId;
        BackgroundColor = EventColours.ToRgbTriplet(details.Colour);
        PublishToSite = details.PublishToSite;
        IsFeatured = details.IsFeatured;
        ExternalUrl = string.IsNullOrWhiteSpace(details.ExternalUrl) ? null : details.ExternalUrl.Trim();
        TourId = details.TourId;
        FollowsTourDates = details.TourId != null && details.FollowsTourDates;
        ShortDescription = details.ShortDescription.NormalizeRichText();
        Description = details.Description.NormalizeRichText();

        AllDay = details.AllDay;
        StartDate = details.AllDay ? details.StartDate.Date : details.StartDate;
        EndDate = details.AllDay ? details.EndDate?.Date : details.EndDate;
        AllProperties = details.AllProperties;

        RecurrenceType = details.RecurrenceType;
        var repeats = details.RecurrenceType != EventRecurrenceType.None;
        RecurrenceInterval = repeats ? Math.Max(1, details.RecurrenceInterval ?? 1) : null;
        RecurrenceDaysOfWeek = details.RecurrenceType == EventRecurrenceType.Weekly && details.RecurrenceDaysOfWeek is > 0
            ? details.RecurrenceDaysOfWeek : null;
        RecurrenceEndDate = repeats ? details.RecurrenceEndDate?.Date : null;
    }

    public void SetImage(string? url) => ImagePath = Common.ImageUrls.Sized(url, Common.ImageUrls.Full);
}

public sealed record EventDetails
{
    public required string Name { get; init; }
    public DefaultIdType? EventCategoryId { get; init; }
    /// <summary>Hex, e.g. "#48bb78"; it wins over the category's colour. Null to use the category's.</summary>
    public string? Colour { get; init; }
    public bool PublishToSite { get; init; }
    public bool IsFeatured { get; init; }
    public string? ExternalUrl { get; init; }
    public DefaultIdType? TourId { get; init; }
    public bool FollowsTourDates { get; init; }
    public string? ShortDescription { get; init; }
    public string? Description { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public bool AllDay { get; init; }
    public bool AllProperties { get; init; }
    public EventRecurrenceType RecurrenceType { get; init; }
    public int? RecurrenceInterval { get; init; }
    public int? RecurrenceDaysOfWeek { get; init; }
    public DateTime? RecurrenceEndDate { get; init; }
}
