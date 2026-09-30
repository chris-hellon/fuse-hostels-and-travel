using Heron.MudCalendar;
using Fuse.Contracts.Calendar;

namespace Fuse.Admin.Pages.Calendar;

/// <summary>A tour departure on the booking calendar, or (month view) a day's "+N more" link to its day view.</summary>
public sealed class DepartureCalendarItem : CalendarItem
{
    public CalendarDeparture? Departure { get; init; }

    /// <summary>For the "+N more" item: how many departures that day aren't shown in the month.</summary>
    public int More { get; init; }
}
