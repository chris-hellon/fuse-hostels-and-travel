using Heron.MudCalendar;

namespace Fuse.Admin.Pages.Events;

/// <summary>One event date on the Events calendar.</summary>
public sealed class EventCalendarItem : CalendarItem
{
    public DefaultIdType EventId { get; init; }
    public string? Colour { get; init; }
    public string? CategoryName { get; init; }
}
