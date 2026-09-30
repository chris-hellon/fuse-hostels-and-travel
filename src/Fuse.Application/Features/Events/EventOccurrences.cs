using Fuse.Domain.Catalog.Events;
using Fuse.Shared.Events;

namespace Fuse.Application.Features.Events;

/// <summary>One date of an event, with what the calendars show about it.</summary>
internal sealed record ExpandedOccurrence(
    DefaultIdType EventId, string Name, DateTime Start, DateTime End, bool AllDay, string? Colour, string? CategoryName,
    string? ShortDescription, string? ImagePath, string? ExternalUrl);

/// <summary>Events expanded from their schedules into dates, for the admin and public calendars.</summary>
internal static class EventOccurrences
{
    /// <summary>The calendars ask for a month or so (the iCal feed six months); never expand more than this.</summary>
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(200);

    public static async Task<IReadOnlyList<ExpandedOccurrence>> ExpandAsync(
        IAppDbContext db, IQueryable<Event> source, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        if (to - from > MaxWindow)
            to = from + MaxWindow;
        if (to < from)
            return [];

        // Anything that could land in the window: repeating events not yet ended, one-offs starting in it,
        // and undated legacy one-offs (see below).
        var events = await source.AsNoTracking()
            .Where(e => (e.FollowsTourDates && e.TourId != null)
                        || (e.StartDate <= to
                            && (e.RecurrenceType == EventRecurrenceType.None || e.RecurrenceEndDate == null || e.RecurrenceEndDate >= from.Date)))
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.StartDate,
                e.EndDate,
                e.AllDay,
                e.RecurrenceType,
                e.RecurrenceInterval,
                e.RecurrenceDaysOfWeek,
                e.RecurrenceEndDate,
                e.BackgroundColor,
                e.ShortDescription,
                e.ImagePath,
                e.ExternalUrl,
                FollowsTour = e.FollowsTourDates ? e.TourId : null,
                CategoryName = e.EventCategory == null ? null : e.EventCategory.Name,
                CategoryColour = e.EventCategory == null ? null : e.EventCategory.Color,
                Cancelled = e.OccurrenceExceptions.Select(x => x.OccurrenceDate).ToList(),
                Slots = e.TimeSlots.OrderBy(x => x.SortOrder).ThenBy(x => x.StartTime).Select(x => new { x.StartTime, x.EndTime }).ToList(),
            })
            .ToListAsync(cancellationToken);

        // Events on their tour's departures: those departures in the window.
        var tourIds = events.Where(e => e.FollowsTour != null).Select(e => e.FollowsTour!.Value).Distinct().ToList();
        var departures = tourIds.Count == 0 ? null : (await db.TourDates.AsNoTracking()
                .Where(d => tourIds.Contains(d.TourId) && d.StartDate >= from && d.StartDate <= to)
                .Select(d => new { d.TourId, d.StartDate, d.EndDate })
                .ToListAsync(cancellationToken))
            .ToLookup(d => d.TourId);

        var occurrences = new List<ExpandedOccurrence>();
        foreach (var e in events)
        {
            if (e.FollowsTour is { } tour)
            {
                var off = e.Cancelled.Select(d => d.Date).ToHashSet();
                occurrences.AddRange(departures![tour].Where(d => !off.Contains(d.StartDate.Date)).Select(d => new ExpandedOccurrence(
                    e.Id, e.Name ?? "", d.StartDate, d.EndDate is { } end && end > d.StartDate ? end : d.StartDate, false,
                    EventColours.For(e.BackgroundColor, e.CategoryColour), e.CategoryName, e.ShortDescription, e.ImagePath, e.ExternalUrl)));
                continue;
            }

            var duration = e.EndDate is { } end && end > e.StartDate ? end - e.StartDate : TimeSpan.Zero;
            var colour = EventColours.For(e.BackgroundColor, e.CategoryColour);

            // Times of day (e.g. Happy Hour at 12pm, 5pm and 7pm), each ending at its own end time (or a point in time).
            var times = e.AllDay || e.Slots.Count == 0 ? null : e.Slots.Select(x => x.StartTime).ToList();
            var ends = e.Slots.Where(x => x.EndTime > x.StartTime).GroupBy(x => x.StartTime).ToDictionary(g => g.Key, g => g.First().EndTime!.Value);
            DateTime End(DateTime start) =>
                times is null ? start + duration
                : ends.TryGetValue(start.TimeOfDay, out var slotEnd) ? start.Date + slotEnd
                : start;

            // As legacy: an old one-off with no end whose only date has passed was never given a real
            // schedule, and shows every day (not one given times of day, which was set up on purpose).
            var undated = e.RecurrenceType == EventRecurrenceType.None && e.EndDate is null && times is null && e.StartDate.Date < from.Date;
            var dates = undated
                ? EveryDay(from, to, e.StartDate.TimeOfDay, e.Cancelled)
                : EventRecurrence.Expand(e.RecurrenceType, e.StartDate, e.RecurrenceEndDate, e.RecurrenceInterval,
                    e.RecurrenceDaysOfWeek, e.Cancelled, from, to, timesOfDay: times);

            occurrences.AddRange(dates.Select(start => new ExpandedOccurrence(
                e.Id, e.Name ?? "", start, End(start), e.AllDay, colour, e.CategoryName, e.ShortDescription, e.ImagePath, e.ExternalUrl)));
        }

        return [.. occurrences.OrderBy(o => o.Start)];
    }

    private static IReadOnlyList<DateTime> EveryDay(DateTime from, DateTime to, TimeSpan timeOfDay, IEnumerable<DateTime> cancelled)
    {
        var off = cancelled.Select(d => d.Date).ToHashSet();
        var dates = new List<DateTime>();
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
            if (!off.Contains(d))
                dates.Add(d + timeOfDay);
        return dates;
    }
}
