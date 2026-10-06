using Fuse.Contracts.Site;

namespace Fuse.Site.Components.Events;

/// <summary>
/// The events calendar's "Every night" strip: events on every day of a run, which would otherwise fill every cell of
/// the month (the BBQ, happy hour, the bar hop). Weekly and other partial patterns stay in the grid.
/// </summary>
public static class NightlyEvents
{
    /// <summary>The shortest run that counts (a three-day festival isn't "every night").</summary>
    public const int MinimumDays = 7;

    /// <summary>
    /// The events that, within <paramref name="month"/>, are on every day from their first date to their last, over at
    /// least <see cref="MinimumDays"/> days: daily events, and those following a tour that departs daily.
    /// </summary>
    public static IReadOnlySet<DefaultIdType> In(IEnumerable<SiteEventOccurrence> occurrences, DateTime month)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        var next = first.AddMonths(1);
        return occurrences
            .Where(o => o.Start >= first && o.Start < next)
            .GroupBy(o => o.EventId)
            .Where(g =>
            {
                var days = g.Select(o => o.Start.Date).Distinct().ToList();
                var span = (days.Max() - days.Min()).Days + 1;
                return span >= MinimumDays && days.Count == span;
            })
            .Select(g => g.Key)
            .ToHashSet();
    }
}
