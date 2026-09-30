using System.Text;
using System.Text.RegularExpressions;
using Mediator;
using Fuse.Contracts.Site;

namespace Fuse.Web.Api;

/// <summary>
/// Legacy /events/calendar.ics: the next six months of published events as an iCalendar feed ("Add to your
/// calendar"), for a hostel (<c>propertyId</c>), one event (<c>eventId</c>) or everything.
/// </summary>
public static partial class EventsCalendarFeedEndpoint
{
    public const string Route = "/events/calendar.ics";

    public static void MapEventsCalendarFeed(this IEndpointRouteBuilder app) =>
        app.MapGet(Route, async (HttpRequest request, ISender sender, Guid? propertyId, Guid? eventId, CancellationToken cancellationToken) =>
            {
                var from = DateTime.Today;
                var occurrences = await sender.Send(new ListSiteEventOccurrences(from, from.AddMonths(6),
                    propertyId == Guid.Empty ? null : propertyId, eventId == Guid.Empty ? null : eventId), cancellationToken);

                var host = $"{request.Scheme}://{request.Host}";
                var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
                var ics = new StringBuilder()
                    .Append("BEGIN:VCALENDAR\r\n")
                    .Append("VERSION:2.0\r\n")
                    .Append("PRODID:-//Fuse Hostels & Travel//Events//EN\r\n")
                    .Append("CALSCALE:GREGORIAN\r\n")
                    .Append("METHOD:PUBLISH\r\n")
                    .Append("X-WR-CALNAME:Fuse Hostels Events\r\n");

                foreach (var o in occurrences)
                {
                    ics.Append("BEGIN:VEVENT\r\n")
                        .Append($"UID:{o.EventId:N}-{o.Start:yyyyMMddTHHmmss}@fusehostelsandtravel.com\r\n")
                        .Append($"DTSTAMP:{stamp}\r\n")
                        .Append(o.AllDay ? $"DTSTART;VALUE=DATE:{o.Start:yyyyMMdd}\r\n" : $"DTSTART:{o.Start:yyyyMMdd'T'HHmmss}\r\n")
                        .Append(o.AllDay ? $"DTEND;VALUE=DATE:{o.Start.Date.AddDays(1):yyyyMMdd}\r\n" : $"DTEND:{(o.End > o.Start ? o.End : o.Start.AddHours(2)):yyyyMMdd'T'HHmmss}\r\n")
                        .Append($"SUMMARY:{Escape(o.Name)}\r\n");
                    var description = Escape(Html().Replace(o.ShortDescription ?? "", "").Trim());
                    if (description.Length > 0)
                        ics.Append($"DESCRIPTION:{description}\r\n");
                    ics.Append($"URL:{host}{o.Url}\r\n")
                        .Append("END:VEVENT\r\n");
                }

                ics.Append("END:VCALENDAR\r\n");
                return Results.Text(ics.ToString(), "text/calendar", Encoding.UTF8);
            })
            .AllowAnonymous();

    /// <summary>RFC 5545 text: backslash, semicolon, comma and new lines escaped.</summary>
    private static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,")
            .Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

    [GeneratedRegex("<.*?>")]
    private static partial Regex Html();
}
