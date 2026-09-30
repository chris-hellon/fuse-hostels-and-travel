using Fuse.Contracts.Site;

namespace Fuse.Site.Components.Events;

/// <summary>An event's dates as visitors read them, in the hostels' time.</summary>
public static class EventDates
{
    /// <summary>Vietnam time (UTC+7, no daylight saving), on the server and in the browser alike.</summary>
    public static DateTime Now => DateTime.UtcNow.AddHours(7);

    /// <summary>The next date not yet over (the list is cached for a while, so skip any passed since).</summary>
    public static SiteEventDate? Next(SiteEvent item) =>
        item.Upcoming.FirstOrDefault(d => (d.End > d.Start ? d.End : d.Start) >= Now);

    /// <summary>"Tonight, 7pm", "Tomorrow, 7pm – 9pm", "Sat 26 Sep, all day".</summary>
    public static string Describe(SiteEvent item, SiteEventDate date) =>
        $"{(Day(date.Start) is var day && day.Length > 3 ? day : date.Start.ToString("ddd d MMM"))}, {Time(item, date)}";

    /// <summary>
    /// Where Book goes for an event booked as a tour: <paramref name="date"/>'s departure (the booking card opens on it),
    /// else the tour's page to pick one. Null when it isn't booked as a tour.
    /// </summary>
    public static string? BookLink(SiteEventBooking? booking, SiteEventDate? date) =>
        booking is null ? null
        : date?.DepartureId is { } departure ? $"/tours/{booking.TourSlug}/{departure}"
        : $"/tours/{booking.TourSlug}";

    /// <summary>What the date costs (its departure's price), else the tour's; null when not booked as a tour or unpriced.</summary>
    public static decimal? Price(SiteEventBooking? booking, SiteEventDate? date) =>
        booking is null ? null : date?.Price ?? booking.Price;

    /// <summary>"$6.50", or null.</summary>
    public static string? PriceText(SiteEventBooking? booking, SiteEventDate? date) =>
        Price(booking, date) is { } price ? Booking.Money.Usd(price) : null;

    /// <summary>"BOOK · $6.50" (just "BOOK" when priced on request).</summary>
    public static string BookLabel(SiteEventBooking? booking, SiteEventDate? date) =>
        PriceText(booking, date) is { } price ? $"BOOK · {price}" : "BOOK";

    /// <summary>"Tonight", "Today", "Tomorrow", else the weekday ("Sat").</summary>
    public static string Day(DateTime start) =>
        start.Date == Now.Date ? (start.Hour >= 17 ? "Tonight" : "Today")
        : start.Date == Now.Date.AddDays(1) ? "Tomorrow"
        : start.ToString("ddd");

    /// <summary>"7pm", "7pm – 9pm", "all day".</summary>
    public static string Time(SiteEvent item, SiteEventDate date) =>
        item.AllDay ? "all day"
        : date.End > date.Start ? $"{Clock(date.Start)} – {Clock(date.End)}"
        : Clock(date.Start);

    /// <summary>
    /// Every time of day it's on, for an event on several times a day ("12pm – 1pm, 5pm – 6pm &amp; 7pm"); else
    /// <paramref name="date"/>'s time.
    /// </summary>
    public static string Times(SiteEvent item, SiteEventDate date)
    {
        if (item.Times.Count < 2)
            return Time(item, date);
        var times = item.Times.Select(t => t.End > t.Start ? $"{Clock(t.Start)} – {Clock(t.End.Value)}" : Clock(t.Start)).ToList();
        return $"{string.Join(", ", times[..^1])} & {times[^1]}";
    }

    /// <summary>The days after <paramref name="next"/>'s: "SAT 26  ·  SUN 27  ·  THU 1 OCT", the month only where it changes.</summary>
    public static string Later(SiteEvent item, SiteEventDate next)
    {
        var days = item.Upcoming.Select(d => d.Start.Date).Distinct().SkipWhile(d => d != next.Start.Date).ToList();
        return string.Join("  ·  ", days.Skip(1).Select((d, i) =>
            d.ToString(d.Month == days[i].Month ? "ddd d" : "ddd d MMM").ToUpperInvariant()));
    }

    /// <summary>"7pm", "7:30pm".</summary>
    private static string Clock(DateTime time) =>
        (time.Minute == 0 ? time.ToString("h tt") : time.ToString("h:mm tt")).Replace(" ", "").ToLowerInvariant();

    private static string Clock(TimeSpan time) => Clock(DateTime.MinValue + time);
}
