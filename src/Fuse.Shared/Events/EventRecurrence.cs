namespace Fuse.Shared.Events;

/// <summary>
/// Expands an event's recurrence rule into concrete occurrence dates for a window — computed on the fly, never
/// stored. Shared by the server (calendar, website, iCal) and the admin wizard's preview so they always agree.
/// </summary>
public static class EventRecurrence
{
    /// <summary>Hard cap so an open-ended rule can't run away.</summary>
    private const int MaxOccurrences = 750;

    /// <summary>
    /// The occurrence start date-times between <paramref name="from"/> and <paramref name="to"/> (inclusive),
    /// honouring the interval, weekly day mask and end date, and skipping cancelled dates. Never more than a
    /// year past <paramref name="today"/> (default: today). With <paramref name="timesOfDay"/>, each day the rule
    /// lands on is on once per time (an event with three times a day gives three a day); otherwise at the start's time.
    /// </summary>
    public static IReadOnlyList<DateTime> Expand(
        EventRecurrenceType type,
        DateTime start,
        DateTime? recurrenceEnd,
        int? interval,
        int? daysOfWeekMask,
        IEnumerable<DateTime>? cancelledDates,
        DateTime from,
        DateTime to,
        DateTime? today = null,
        IReadOnlyCollection<TimeSpan>? timesOfDay = null)
    {
        var cancelled = cancelledDates?.Select(d => d.Date).ToHashSet() ?? [];
        var results = new List<DateTime>();

        // A hard ceiling: never past the requested window, the rule's own end, or a year out.
        var ceiling = new[] { to, recurrenceEnd ?? DateTime.MaxValue, (today ?? DateTime.Today).AddYears(1).Add(to.TimeOfDay) }.Min();

        var times = timesOfDay is { Count: > 0 } ? timesOfDay.Distinct().Order().ToList() : [start.TimeOfDay];

        // A day the rule lands on: once per time, unless the day is cancelled or outside the window.
        void TryAdd(DateTime day)
        {
            if (cancelled.Contains(day.Date))
                return;
            foreach (var time in times)
            {
                var occurrence = day.Date + time;
                if (occurrence >= from && occurrence <= to && occurrence.Date <= ceiling.Date && results.Count < MaxOccurrences)
                    results.Add(occurrence);
            }
        }

        if (type == EventRecurrenceType.None)
        {
            TryAdd(start);
            return results;
        }

        var step = Math.Max(1, interval ?? 1);

        switch (type)
        {
            case EventRecurrenceType.Daily:
                for (var d = start; d.Date <= ceiling.Date && results.Count < MaxOccurrences; d = d.AddDays(step))
                    TryAdd(d);
                break;

            case EventRecurrenceType.Weekly:
                // Each selected weekday within every Nth week (weeks counted from the start date's week).
                var mask = EffectiveDays(daysOfWeekMask, start);
                var weekStart = start.Date.AddDays(-(int)start.DayOfWeek); // Sunday of the start's week
                for (var w = weekStart; w <= ceiling.Date && results.Count < MaxOccurrences; w = w.AddDays(7 * step))
                {
                    for (var dow = 0; dow < 7; dow++)
                    {
                        if ((mask & (1 << dow)) == 0)
                            continue;
                        var day = w.AddDays(dow);
                        if (day >= start.Date)
                            TryAdd(day);
                    }
                }
                break;

            case EventRecurrenceType.Monthly:
                // Same day of the month as the start, every Nth month. Counted from the start each time, so a
                // 31st start lands on the last day of short months and back on the 31st afterwards.
                for (var i = 0; results.Count < MaxOccurrences; i += step)
                {
                    var day = start.Date.AddMonths(i);
                    if (day > ceiling.Date)
                        break;
                    TryAdd(day);
                }
                break;
        }

        results.Sort();
        return results;
    }

    /// <summary>A weekly rule's days: none selected means the start date's weekday.</summary>
    public static int EffectiveDays(int? daysOfWeekMask, DateTime start) =>
        daysOfWeekMask is > 0 ? daysOfWeekMask.Value : 1 << (int)start.DayOfWeek;

    public static bool Includes(int mask, DayOfWeek day) => (mask & (1 << (int)day)) != 0;

    /// <summary>Plain-English rule, e.g. "Every 2 weeks on Mon, Fri until 3 Oct 2026".</summary>
    public static string Describe(EventRecurrenceType type, DateTime start, int? interval, int? daysOfWeekMask, DateTime? recurrenceEnd)
    {
        if (type == EventRecurrenceType.None)
            return "Does not repeat";

        var n = Math.Max(1, interval ?? 1);
        var unit = type switch
        {
            EventRecurrenceType.Daily => "day",
            EventRecurrenceType.Weekly => "week",
            _ => "month",
        };
        var text = n == 1 ? $"Every {unit}" : $"Every {n} {unit}s";

        if (type == EventRecurrenceType.Weekly)
        {
            var mask = EffectiveDays(daysOfWeekMask, start);
            var days = WeekDays.Where(d => Includes(mask, d)).Select(d => d.ToString()[..3]);
            text += $" on {string.Join(", ", days)}";
        }
        else if (type == EventRecurrenceType.Monthly)
        {
            text += $" on the {Ordinal(start.Day)}";
        }

        return recurrenceEnd is { } end ? $"{text} until {end:d MMM yyyy}" : text;
    }

    /// <summary>
    /// The rule for visitors, e.g. "Every Mon & Wed", "Every day", "Every 2 weeks on Fri", "Monthly on the 3rd";
    /// null for a one-off.
    /// </summary>
    public static string? Summary(EventRecurrenceType type, DateTime start, int? interval, int? daysOfWeekMask)
    {
        var n = Math.Max(1, interval ?? 1);
        switch (type)
        {
            case EventRecurrenceType.Daily:
                return n == 1 ? "Every day" : $"Every {n} days";
            case EventRecurrenceType.Weekly:
                var mask = EffectiveDays(daysOfWeekMask, start);
                var days = WeekDays.Where(d => Includes(mask, d)).Select(d => d.ToString()[..3]).ToList();
                if (days.Count == 7)
                    return n == 1 ? "Every day" : $"Every day, every {n} weeks";
                var list = days.Count == 1 ? days[0] : $"{string.Join(", ", days[..^1])} & {days[^1]}";
                return n == 1 ? $"Every {list}" : $"Every {n} weeks on {list}";
            case EventRecurrenceType.Monthly:
                return n == 1 ? $"Monthly on the {Ordinal(start.Day)}" : $"Every {n} months on the {Ordinal(start.Day)}";
            default:
                return null;
        }
    }

    /// <summary>Monday first, as the admin shows them.</summary>
    public static readonly IReadOnlyList<DayOfWeek> WeekDays =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    private static string Ordinal(int day) => day switch
    {
        1 or 21 or 31 => $"{day}st",
        2 or 22 => $"{day}nd",
        3 or 23 => $"{day}rd",
        _ => $"{day}th",
    };
}
