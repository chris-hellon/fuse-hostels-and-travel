using Fuse.Application.Common.Time;
using Fuse.Contracts.Site;
using Fuse.Domain.Catalog.Events;
using Fuse.Shared.Events;
using Fuse.Shared.Text;

namespace Fuse.Application.Features.Site;

/// <summary>Published events as the site shows them (What's On, an event's own page), with their next dates.</summary>
internal static class SiteEvents
{
    public static async Task<List<SiteEvent>> LoadAsync(
        IAppDbContext db, IBusinessClock clock, IQueryable<Event> source, TimeSpan ahead, int upcomingDays, CancellationToken cancellationToken)
    {
        var events = await source.AsNoTracking()
            .OrderByDescending(e => e.StartDate)
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.ShortDescription,
                e.ImagePath,
                e.BackgroundColor,
                e.StartDate,
                e.EndDate,
                e.AllDay,
                e.RecurrenceType,
                e.RecurrenceInterval,
                e.RecurrenceDaysOfWeek,
                e.ExternalUrl,
                e.TourId,
                e.FollowsTourDates,
                e.AllProperties,
                CategoryName = e.EventCategory == null ? null : e.EventCategory.Name,
                CategoryColour = e.EventCategory == null ? null : e.EventCategory.Color,
                Times = e.TimeSlots.OrderBy(x => x.SortOrder).ThenBy(x => x.StartTime).Select(x => new SiteEventTime(x.StartTime, x.EndTime)).ToList(),
                Hostels = e.EventPropertyLookups
                    .Where(l => l.Property.PublishToSite == true)
                    .OrderBy(l => l.Property.Name)
                    .Select(l => new { l.Property.Id, l.Property.Name, l.Property.UrlSlug })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        // Dates still to come (or on now).
        var now = clock.Now;
        var occurrences = (await Events.EventOccurrences.ExpandAsync(db, source, now.Date, now + ahead, cancellationToken))
            .Where(o => (o.End > o.Start ? o.End : o.Start) >= now)
            .ToLookup(o => o.EventId);

        // Events booked as a tour (published or not: the event is how it's found): prices and departures.
        var booking = await EventTours.LoadAsync(db, events.Select(e => e.TourId), now, now + ahead + TimeSpan.FromDays(1), cancellationToken);

        return [.. events.Select(e =>
        {
            IReadOnlyList<SiteEventTime> times = e.AllDay ? [] : e.Times;
            var days = occurrences[e.Id].Select(o => o.Start.Date).Distinct().Take(upcomingDays).ToHashSet();
            return new SiteEvent
            {
                Id = e.Id,
                Name = e.Name ?? "",
                Slug = Slugs.From(e.Name),
                ShortDescription = e.ShortDescription,
                ImagePath = e.ImagePath,
                BackgroundColor = e.BackgroundColor,
                CategoryName = e.CategoryName,
                Colour = EventColours.For(e.BackgroundColor, e.CategoryColour),
                // On the tour's departures: the weekdays it departs over the next four weeks ("Every Mon & Fri").
                // An old one-off never given a schedule shows every day (see EventOccurrences).
                Repeats = e.FollowsTourDates ? DeparturePattern(occurrences[e.Id])
                    : e.RecurrenceType == EventRecurrenceType.None && e.EndDate is null && times.Count == 0 && e.StartDate.Date < now.Date
                    ? "Every day"
                    : EventRecurrence.Summary(e.RecurrenceType, e.StartDate, e.RecurrenceInterval, e.RecurrenceDaysOfWeek),
                AllDay = e.AllDay,
                Upcoming = [.. occurrences[e.Id].Where(o => days.Contains(o.Start.Date)).Select(o => booking.Date(e.TourId, o.Start, o.End))],
                Times = times,
                AllHostels = e.AllProperties == true,
                Hostels = [.. e.Hostels.Select(h => new SiteLink { Id = h.Id, Name = h.Name ?? "", Slug = SiteCache.Slug(h.UrlSlug, h.Name ?? "") })],
                ExternalUrl = e.ExternalUrl,
                Booking = booking.For(e.TourId),
            };
        })];
    }

    /// <summary>"Every day", or the weekdays of the next four weeks' dates ("Every Mon, Wed & Fri"); null with none.</summary>
    private static string? DeparturePattern(IEnumerable<Events.ExpandedOccurrence> dates)
    {
        var all = dates.ToList();
        if (all.Count == 0)
            return null;
        var soon = all.Where(d => d.Start < all[0].Start.Date.AddDays(28)).ToList();
        var mask = soon.Select(d => 1 << (int)d.Start.DayOfWeek).Aggregate(0, (a, b) => a | b);
        return mask == 0b1111111 ? "Every day" : EventRecurrence.Summary(EventRecurrenceType.Weekly, soon[0].Start, 1, mask);
    }
}

internal sealed class ListSiteEventsHandler(IAppDbContext db, SiteCache cache, IBusinessClock clock)
    : IQueryHandler<ListSiteEvents, IReadOnlyList<SiteEvent>>
{
    /// <summary>How far ahead to look for an event's next dates, and how many days of them to give.</summary>
    private static readonly TimeSpan Ahead = TimeSpan.FromDays(90);
    private const int UpcomingDays = 4;

    public async ValueTask<IReadOnlyList<SiteEvent>> Handle(ListSiteEvents query, CancellationToken cancellationToken) =>
        await cache.GetAsync<IReadOnlyList<SiteEvent>>($"site:events:{query.FeaturedOnly}", async () => await SiteEvents.LoadAsync(db, clock,
            db.Events.Where(e => e.PublishToSite == true && (!query.FeaturedOnly || e.IsFeatured == true)), Ahead, UpcomingDays, cancellationToken));
}

/// <summary>Legacy's event page (/events/{name}): the event by its name made URL-friendly, with its next eight days.</summary>
internal sealed class GetSiteEventHandler(IAppDbContext db, SiteCache cache, IBusinessClock clock) : IQueryHandler<GetSiteEvent, SiteEventDetails>
{
    private static readonly TimeSpan Ahead = TimeSpan.FromDays(183);
    private const int UpcomingDays = 8;

    public async ValueTask<SiteEventDetails> Handle(GetSiteEvent query, CancellationToken cancellationToken) =>
        await cache.GetAsync($"site:event:{query.Slug.ToLowerInvariant()}", () => LoadAsync(query.Slug, cancellationToken));

    private async Task<SiteEventDetails> LoadAsync(string slug, CancellationToken cancellationToken)
    {
        // Matched on the name made URL-friendly, as legacy (there's no stored slug).
        var published = await db.Events.AsNoTracking().Where(e => e.PublishToSite == true)
            .Select(e => new { e.Id, e.Name, e.Description }).ToListAsync(cancellationToken);
        var match = published.FirstOrDefault(e => Slugs.From(e.Name).Equals(slug, StringComparison.OrdinalIgnoreCase))
                    ?? throw new NotFoundException("Event not found.");

        var @event = (await SiteEvents.LoadAsync(db, clock, db.Events.Where(e => e.Id == match.Id), Ahead, UpcomingDays, cancellationToken)).Single();
        return new SiteEventDetails(@event, match.Description);
    }
}

/// <summary>
/// The tours events are booked as: each one's price (its lowest on the website, else its lowest) and whether it can be
/// booked online, and its departures, to match to each date the event is on (the one starting nearest the event).
/// </summary>
internal sealed class EventTours
{
    private sealed record TourRow(DefaultIdType Id, string Name, string? UrlSlug, decimal? WebPrice, decimal? AnyPrice);
    private sealed record DepartureRow(DefaultIdType Id, DefaultIdType TourId, DateTime Start, decimal Price, int Spaces, bool OnWebsite);

    private readonly Dictionary<DefaultIdType, TourRow> _tours;
    private readonly ILookup<(DefaultIdType, DateTime), DepartureRow> _departures;

    private EventTours(Dictionary<DefaultIdType, TourRow> tours, ILookup<(DefaultIdType, DateTime), DepartureRow> departures) =>
        (_tours, _departures) = (tours, departures);

    public static async Task<EventTours> LoadAsync(IAppDbContext db, IEnumerable<DefaultIdType?> tourIds, DateTime from, DateTime until, CancellationToken cancellationToken)
    {
        var ids = tourIds.OfType<DefaultIdType>().Distinct().ToList();
        if (ids.Count == 0)
            return new EventTours([], Array.Empty<DepartureRow>().ToLookup(d => (d.TourId, d.Start.Date)));
        var tours = await db.Tours.AsNoTracking().Where(t => ids.Contains(t.Id))
            .Select(t => new TourRow(t.Id, t.Name, t.UrlSlug,
                db.TourPrices.Where(p => p.TourId == t.Id && p.PublishToWebsite == true && p.InHouseOnly != true).Min(p => (decimal?)p.Price),
                db.TourPrices.Where(p => p.TourId == t.Id).Min(p => (decimal?)p.Price)))
            .ToDictionaryAsync(t => t.Id, cancellationToken);
        var departures = await db.TourDates.AsNoTracking()
            .Where(d => ids.Contains(d.TourId) && d.StartDate >= from && d.StartDate < until)
            .Select(d => new DepartureRow(d.Id, d.TourId, d.StartDate, d.PriceOverride ?? d.TourPrice.Price, d.AvailableSpaces,
                d.TourPrice.PublishToWebsite == true && d.TourPrice.InHouseOnly != true))
            .ToListAsync(cancellationToken);
        return new EventTours(tours, departures.ToLookup(d => (d.TourId, d.Start.Date)));
    }

    /// <summary>
    /// The event's tour: its page, price, and whether it's bookable online (a web price and a departure on the website
    /// to come); null when it isn't booked as a tour.
    /// </summary>
    public SiteEventBooking? For(DefaultIdType? tourId) =>
        tourId is { } id && _tours.TryGetValue(id, out var tour)
            ? new SiteEventBooking(SiteCache.Slug(tour.UrlSlug, tour.Name), tour.WebPrice ?? tour.AnyPrice, tour.Id, tour.Name,
                Online: tour.WebPrice is not null && _departures.Any(g => g.Key.Item1 == id && g.Any(d => d.OnWebsite)))
            : null;

    /// <summary>
    /// One time the event's on: that day's departure (its price; its id when it can be booked on the website), else the
    /// tour's price.
    /// </summary>
    public SiteEventDate Date(DefaultIdType? tourId, DateTime start, DateTime end)
    {
        if (tourId is not { } id || !_tours.TryGetValue(id, out var tour))
            return new SiteEventDate(start, end);
        var departure = _departures[(id, start.Date)].OrderBy(d => Math.Abs((d.Start - start).Ticks)).FirstOrDefault();
        return new SiteEventDate(start, end, departure is { OnWebsite: true } ? departure.Id : null, departure is { Spaces: <= 0 },
            departure?.Price ?? tour.WebPrice ?? tour.AnyPrice);
    }
}
