using System.Globalization;
using Mediator;
using Fuse.Contracts.Common;
using Fuse.Contracts.Site;

namespace Fuse.Web.Api;

/// <summary>
/// Legacy /hostel-booking/{hostel}[/{userId}]?checkin=…&amp;checkout=…: sends the guest to the hostel's Cloudbeds
/// booking page (the site's Book Now forms go here). The old user id segment is ignored; a signed-in guest's
/// details are filled in instead. With Site:HostelBooking = Embed, goes to our own page with Cloudbeds' booking
/// engine in it instead (/hostels/{hostel}/book, which reads the dates from its address).
/// </summary>
public static class HostelBookingEndpoint
{
    public const string Route = "/hostel-booking/{slug}/{userId?}";

    public static void MapHostelBooking(this IEndpointRouteBuilder app) =>
        app.MapGet(Route, async (HttpRequest request, ISender sender, Microsoft.Extensions.Options.IOptions<Application.Features.Site.SiteOptions> site,
                string slug, string? checkin, string? checkout, CancellationToken cancellationToken) =>
            {
                try
                {
                    if (site.Value.EmbedHostelBooking)
                    {
                        var engine = await sender.Send(new GetHostelBookingEngine(slug), cancellationToken);
                        var dates = string.Join("&", new[] { ("checkin", Date(checkin)), ("checkout", Date(checkout)) }
                            .Where(d => d.Item2 is not null).Select(d => $"{d.Item1}={d.Item2:yyyy-MM-dd}"));
                        return Results.Redirect($"/hostels/{engine.Slug}/book{(dates.Length > 0 ? "?" + dates : "")}");
                    }
                    return Results.Redirect(await sender.Send(new GetHostelBookingLink(slug, Date(checkin), Date(checkout)), cancellationToken));
                }
                catch (NotFoundException)
                {
                    return Results.NotFound();
                }
            })
            .AllowAnonymous();

    private static DateOnly? Date(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
