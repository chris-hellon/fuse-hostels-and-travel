using Fuse.Shared.Tours;

namespace Fuse.Contracts.Tours;

/// <summary>An upcoming departure as the editor shows it.</summary>
public sealed record TourDateItem
{
    public DefaultIdType Id { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    /// <summary>The price's key (its id). Null only for old departures whose price was removed.</summary>
    public DefaultIdType? PriceKey { get; init; }
    public decimal? PriceOverride { get; init; }
    public int AvailableSpaces { get; init; }
    /// <summary>Has bookings: can't be deleted, and only its price override can change.</summary>
    public bool Booked { get; init; }
}

/// <summary>A departure to add, or a change to an existing one (<see cref="Id"/> set).</summary>
public sealed record TourDateEdit
{
    public DefaultIdType? Id { get; init; }
    public DefaultIdType PriceKey { get; init; }
    public DateTime StartDate { get; init; }
    public decimal? PriceOverride { get; init; }
}

public enum RepeatEvery
{
    Day,
    Week,
    Month,
}

/// <summary>Turns "every N days/weeks/months from A to B at these times" into start times.</summary>
public static class TourSchedule
{
    /// <summary>The most departures one save may add.</summary>
    public const int MaxNewDates = 3000;

    public static IEnumerable<DateTime> Starts(DateOnly first, DateOnly last, RepeatEvery every, int interval, IEnumerable<TimeOnly> times)
    {
        var distinctTimes = times.Distinct().Order().ToList();
        // Months step from the first date (so the 31st stays the 31st where it exists), as legacy did.
        for (var i = 0; ; i++)
        {
            var day = every switch
            {
                RepeatEvery.Week => first.AddDays(7 * interval * i),
                RepeatEvery.Month => first.AddMonths(interval * i),
                _ => first.AddDays(interval * i),
            };
            if (day > last)
                yield break;
            foreach (var time in distinctTimes)
                yield return day.ToDateTime(time);
        }
    }

    public static DateTime EndFor(DateTime start, TourPriceInput price) =>
        TourDurations.EndFor(start, price.DayDuration, price.NightDuration, price.HourDuration);
}
