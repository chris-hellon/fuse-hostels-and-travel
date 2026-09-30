using Fuse.Shared.Events;

namespace Fuse.Contracts.Events;

public sealed record EventSummary
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    public string? CategoryName { get; init; }
    public string? CategoryColour { get; init; }
    /// <summary>As stored: legacy "r, g, b" (or hex).</summary>
    public string? BackgroundColour { get; init; }
    public DateTime StartDate { get; init; }
    public bool AllDay { get; init; }
    public EventRecurrenceType RecurrenceType { get; init; }
    public int? RecurrenceInterval { get; init; }
    public int? RecurrenceDaysOfWeek { get; init; }
    public DateTime? RecurrenceEndDate { get; init; }
    public bool PublishToSite { get; init; }
    public bool IsFeatured { get; init; }
    public bool FollowsTourDates { get; init; }

    /// <summary>Calendar colour (hex): the event's own, else its category's. Same rule as the calendar.</summary>
    [NotSortable]
    public string? Colour => EventColours.For(BackgroundColour, CategoryColour);

    [NotSortable]
    public string Repeats => FollowsTourDates ? "On the tour's departures"
        : EventRecurrence.Describe(RecurrenceType, StartDate, RecurrenceInterval, RecurrenceDaysOfWeek, RecurrenceEndDate);
}

[Post("events/search"), Permission(FuseAction.Search, FuseResource.Events)]
public sealed record SearchEvents : PagedQuery, IQuery<PagedResult<EventSummary>>
{
    public DefaultIdType? EventCategoryId { get; init; }
}

public sealed record EventInput
{
    public string Name { get; set; } = "";
    public DefaultIdType? EventCategoryId { get; set; }
    /// <summary>Hex colour on the site and calendars; none = the category's colour.</summary>
    public string? Colour { get; set; }
    public bool PublishToSite { get; set; }
    public bool IsFeatured { get; set; }
    /// <summary>A Facebook event, ticket page...</summary>
    public string? ExternalUrl { get; set; }
    /// <summary>A tour it's booked as: each date on the site links to that day's departure.</summary>
    public DefaultIdType? TourId { get; set; }
    /// <summary>With a tour: the event is on whenever the tour departs (instead of its own dates).</summary>
    public bool FollowsTourDates { get; set; }
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool AllDay { get; set; }

    public EventRecurrenceType RecurrenceType { get; set; }
    public int? RecurrenceInterval { get; set; }
    /// <summary>Weekly only: bit (1 &lt;&lt; (int)DayOfWeek) per day; none = the start date's weekday.</summary>
    public int? RecurrenceDaysOfWeek { get; set; }
    public DateTime? RecurrenceEndDate { get; set; }
    /// <summary>Occurrences of a repeating event that are off.</summary>
    public List<DateTime> CancelledDates { get; set; } = [];

    /// <summary>
    /// Times of day it's on (e.g. Happy Hour at 12pm, 5pm and 7pm): once per time on every day the rule lands on.
    /// None = at the start time (and until the end). Ignored for an all-day event.
    /// </summary>
    public List<EventTimeSlotInput> TimeSlots { get; set; } = [];

    /// <summary>At every property; <see cref="PropertyIds"/> is then ignored.</summary>
    public bool AllProperties { get; set; }
    public List<DefaultIdType> PropertyIds { get; set; } = [];
}

/// <summary>A time of day an event is on; no end = a point in time.</summary>
public sealed class EventTimeSlotInput
{
    public TimeSpan StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
}

public sealed record EventEditor(DefaultIdType Id, EventInput Details, string? ImagePath);

[Get("events/{Id}"), Permission(FuseAction.View, FuseResource.Events)]
public sealed record GetEventEditor(DefaultIdType Id) : IQuery<EventEditor>;

/// <summary>
/// Creates (no <see cref="Id"/>) or updates an event with its properties, cancelled dates and image in
/// one go (the wizard's Save). <see cref="Image"/> replaces the image; <see cref="RemoveImage"/> clears it.
/// </summary>
[Put("events"), Permission(FuseAction.Update, FuseResource.Events)]
public sealed record SaveEvent(DefaultIdType? Id, EventInput Details, FileUpload? Image, bool RemoveImage) : ICommand<DefaultIdType>;

[Delete("events/{Id}"), Permission(FuseAction.Delete, FuseResource.Events)]
public sealed record DeleteEvent(DefaultIdType Id) : ICommand;

/// <summary>One date of an event, expanded from its schedule (the calendar view).</summary>
public sealed record EventOccurrence(DefaultIdType EventId, string Name, DateTime Start, DateTime End, bool AllDay, string? Colour, string? CategoryName);

[Post("events/occurrences"), Permission(FuseAction.View, FuseResource.Events)]
public sealed record ListEventOccurrences(DateTime From, DateTime To) : IQuery<IReadOnlyList<EventOccurrence>>;

/// <summary>Properties an event can be at.</summary>
[Get("events/lookups/properties"), Permission(FuseAction.View, FuseResource.Events)]
public sealed record ListEventProperties : IQuery<IReadOnlyList<Tours.LookupItem>>;

/// <summary>A departure of the tour an event is booked as (the Schedule step lists them).</summary>
public sealed record EventTourDeparture(DateTime Start, DateTime? End, int AvailableSpaces, bool OnWebsite);

/// <summary>The tour's departures from today, for an event that follows them.</summary>
[Get("events/lookups/tours/{TourId}/departures"), Permission(FuseAction.View, FuseResource.Events)]
public sealed record ListEventTourDepartures(DefaultIdType TourId) : IQuery<IReadOnlyList<EventTourDeparture>>;

/// <summary>Tours an event can be booked as: every tour, including ones not on the website (the event makes them bookable).</summary>
[Get("events/lookups/tours"), Permission(FuseAction.View, FuseResource.Events)]
public sealed record ListEventTours : IQuery<IReadOnlyList<Tours.LookupItem>>;

public sealed class EventInputValidator : AbstractValidator<EventInput>
{
    public const int ShortDescriptionMax = 500;

    public EventInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.ShortDescription).MaximumLength(ShortDescriptionMax).WithName("Short description")
            .WithMessage($"Short description is too long (the limit, formatting included, is {ShortDescriptionMax} characters).");
        RuleFor(x => x.Colour).Matches("^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$").When(x => !string.IsNullOrWhiteSpace(x.Colour))
            .WithMessage("Colour must be a hex colour, e.g. #48bb78.");
        RuleFor(x => x.ExternalUrl)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            .When(x => !string.IsNullOrWhiteSpace(x.ExternalUrl))
            .WithName("Link").WithMessage("Link must be a full web address, starting with https://");
        RuleFor(x => x.StartDate).NotEqual(default(DateTime)).WithName("Start date");
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate).When(x => x.EndDate.HasValue)
            .WithName("End").WithMessage("The event can't end before it starts.");
        RuleFor(x => x.RecurrenceInterval).InclusiveBetween(1, 365).When(x => x.RecurrenceType != EventRecurrenceType.None && x.RecurrenceInterval.HasValue)
            .WithName("Repeat every");
        RuleFor(x => x.RecurrenceEndDate).GreaterThanOrEqualTo(x => x.StartDate.Date)
            .When(x => x.RecurrenceType != EventRecurrenceType.None && x.RecurrenceEndDate.HasValue)
            .WithName("Repeat until").WithMessage("Repeat until can't be before the start date.");
        RuleFor(x => x.RecurrenceDaysOfWeek).InclusiveBetween(0, 127).When(x => x.RecurrenceDaysOfWeek.HasValue);
        RuleForEach(x => x.TimeSlots).ChildRules(slot =>
        {
            slot.RuleFor(s => s.StartTime).InclusiveBetween(TimeSpan.Zero, new TimeSpan(23, 59, 59)).WithName("Time");
            slot.RuleFor(s => s.EndTime).GreaterThan(s => s.StartTime).When(s => s.EndTime.HasValue)
                .WithName("Time").WithMessage("A time can't end before it starts.");
        }).When(x => !x.AllDay);
        RuleFor(x => x.TimeSlots).Must(slots => slots.Select(s => s.StartTime).Distinct().Count() == slots.Count)
            .When(x => !x.AllDay).WithName("Times").WithMessage("Each time must start at a different time.");
    }
}

public sealed class SaveEventValidator : AbstractValidator<SaveEvent>
{
    public SaveEventValidator() => RuleFor(x => x.Details).SetValidator(new EventInputValidator());
}
