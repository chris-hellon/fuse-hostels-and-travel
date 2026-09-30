using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Fuse.Contracts.Site;
using Fuse.Shared.Text;

namespace Fuse.Application.Features.Site;

/// <summary>The "Site" configuration section: the public website's defaults (legacy <c>FuseSettings</c>).</summary>
public sealed class SiteOptions
{
    public const string Section = "Site";

    public string SiteName { get; set; } = "";
    public string WebsiteUrl { get; set; } = "";
    public string? MetaDescription { get; set; }
    public string? MetaKeywords { get; set; }
    public string? MetaImageUrl { get; set; }
    public string? GoogleSiteVerification { get; set; }
    /// <summary>
    /// How Book Now takes hostel bookings: <c>Redirect</c> (default) sends visitors to the hostel's Cloudbeds page;
    /// <c>Embed</c> shows Cloudbeds' booking engine on our own page (Immersive Experience 2.0), which only works on a
    /// domain whitelisted in each hostel's Cloudbeds settings.
    /// </summary>
    public string HostelBooking { get; set; } = "Redirect";

    public bool EmbedHostelBooking => HostelBooking.Equals("Embed", StringComparison.OrdinalIgnoreCase);

    /// <summary>The site's address in links sent by email (e.g. confirming a new email); the website's unless set (development).</summary>
    public string? PublicUrl { get; set; }

    public string LinksUrl => (string.IsNullOrWhiteSpace(PublicUrl) ? WebsiteUrl : PublicUrl).TrimEnd('/');

    public SiteAnalyticsOptions Analytics { get; set; } = new();

    /// <summary>Where the website's enquiries are emailed (legacy MailSettings:ToAddress).</summary>
    public string? ContactEmail { get; set; }
}

/// <summary>The "Site:Analytics" section (the tags themselves are written by the web host's App.razor, production only).</summary>
public sealed class SiteAnalyticsOptions
{
    /// <summary>
    /// Google Ads' "Website traffic conversion" (send_to), counted when the hostel booking page opens (legacy had the
    /// snippet on its booking page, but that page redirected to Cloudbeds, so it never fired).
    /// </summary>
    public string? HostelBookingConversion { get; set; }
}

/// <summary>
/// The public site's queries run on every page view, so in production their results are cached for an hour (legacy
/// cached published hostels and tours the same way); elsewhere they always read the database. Any command clears
/// the cache (<c>SiteCacheBehaviour</c>), so admin changes show on the site straight away.
/// </summary>
public sealed class SiteCache(IMemoryCache memory, IHostEnvironment environment)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    // Every entry expires when this is cancelled; Clear swaps in a new one.
    private CancellationTokenSource _generation = new();

    public async ValueTask<T> GetAsync<T>(string key, Func<Task<T>> load)
    {
        if (!environment.IsProduction())
            return await load();
        if (memory.TryGetValue(key, out T? cached) && cached is not null)
            return cached;
        var generation = _generation.Token;
        var value = await load();
        memory.Set(key, value, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Lifetime }
            .AddExpirationToken(new CancellationChangeToken(generation)));
        return value;
    }

    public void Clear()
    {
        var previous = Interlocked.Exchange(ref _generation, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
    }

    /// <summary>A hostel's or tour's URL segment: its slug, or (as legacy) its name made URL-friendly.</summary>
    public static string Slug(string? urlSlug, string name) => string.IsNullOrWhiteSpace(urlSlug) ? Slugs.From(name) : urlSlug;

    /// <summary>
    /// A /hostel-booking/{hostel} segment: legacy matched only the hostel's name made URL-friendly (the segment made
    /// URL-friendly too), so old links, emails and ads carry that even where the hostel has its own slug; ours
    /// carry the slug. Either finds it.
    /// </summary>
    public static T? FindForBooking<T>(IReadOnlyList<T> hostels, Func<T, string?> urlSlug, Func<T, string> name, string segment) where T : class =>
        hostels.FirstOrDefault(h => Slug(urlSlug(h), name(h)).Equals(segment, StringComparison.OrdinalIgnoreCase))
        ?? (Slugs.From(segment) is { Length: > 0 } byName ? hostels.FirstOrDefault(h => Slugs.From(name(h)) == byName) : null);
}

internal sealed class GetSiteChromeHandler(IAppDbContext db, SiteCache cache, IOptions<SiteOptions> options, IOptions<CaptchaOptions> captcha, IHostEnvironment environment)
    : IQueryHandler<GetSiteChrome, SiteChrome>
{
    public async ValueTask<SiteChrome> Handle(GetSiteChrome query, CancellationToken cancellationToken) =>
        await cache.GetAsync("site:chrome", async () =>
        {
            var hostels = await db.Properties.AsNoTracking()
                .Where(p => p.PublishToSite == true)
                .OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Name, p.UrlSlug, Bookable = p.CloudbedsKey != null })
                .ToListAsync(cancellationToken);
            var tours = await db.Tours.AsNoTracking()
                .Where(t => t.PublishToSite == true)
                .OrderBy(t => t.Name)
                .Select(t => new { t.Id, t.Name, t.UrlSlug })
                .ToListAsync(cancellationToken);
            var seo = await db.Seo.AsNoTracking().Select(s => new { s.Breadcrumbs, s.SearchAction }).FirstOrDefaultAsync(cancellationToken);
            var site = options.Value;

            return new SiteChrome
            {
                Hostels = [.. hostels.Select(h => new SiteLink { Id = h.Id, Name = h.Name, Slug = SiteCache.Slug(h.UrlSlug, h.Name) })],
                Tours = [.. tours.Select(t => new SiteLink { Id = t.Id, Name = t.Name, Slug = SiteCache.Slug(t.UrlSlug, t.Name) })],
                BookableHostels = [.. hostels.Where(h => h.Bookable).Select(h => new SiteLink { Id = h.Id, Name = h.Name, Slug = SiteCache.Slug(h.UrlSlug, h.Name) })],
                CaptchaSiteKey = captcha.Value.SiteKey,
                HostelBookingConversion = site.Analytics.HostelBookingConversion,
                Seo = new SiteSeo
                {
                    SiteName = site.SiteName,
                    WebsiteUrl = site.WebsiteUrl.TrimEnd('/'),
                    MetaDescription = site.MetaDescription,
                    MetaKeywords = site.MetaKeywords,
                    MetaImageUrl = site.MetaImageUrl,
                    GoogleSiteVerification = site.GoogleSiteVerification,
                    Indexable = environment.IsProduction(),
                    Breadcrumbs = seo?.Breadcrumbs,
                    SearchAction = seo?.SearchAction,
                },
            };
        });
}

internal sealed class ListSiteHostelsHandler(IAppDbContext db, SiteCache cache) : IQueryHandler<ListSiteHostels, IReadOnlyList<SiteCard>>
{
    public async ValueTask<IReadOnlyList<SiteCard>> Handle(ListSiteHostels query, CancellationToken cancellationToken) =>
        await cache.GetAsync<IReadOnlyList<SiteCard>>("site:hostels", async () =>
        {
            var hostels = await db.Properties.AsNoTracking()
                .Where(p => p.PublishToSite == true)
                .OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Name, p.UrlSlug, p.ShortDescription, p.ImagePath })
                .ToListAsync(cancellationToken);
            return [.. hostels.Select(h => new SiteCard { Id = h.Id, Name = h.Name, Slug = SiteCache.Slug(h.UrlSlug, h.Name), ShortDescription = h.ShortDescription, ImagePath = h.ImagePath })];
        });
}

internal sealed class ListSiteToursHandler(IAppDbContext db, SiteCache cache) : IQueryHandler<ListSiteTours, IReadOnlyList<SiteCard>>
{
    public async ValueTask<IReadOnlyList<SiteCard>> Handle(ListSiteTours query, CancellationToken cancellationToken) =>
        await cache.GetAsync<IReadOnlyList<SiteCard>>("site:tours", async () =>
        {
            var tours = await db.Tours.AsNoTracking()
                .Where(t => t.PublishToSite == true)
                .OrderBy(t => t.Name)
                .Select(t => new { t.Id, t.Name, t.UrlSlug, t.ShortDescription, t.ImagePath })
                .ToListAsync(cancellationToken);
            return [.. tours.Select(t => new SiteCard { Id = t.Id, Name = t.Name, Slug = SiteCache.Slug(t.UrlSlug, t.Name), ShortDescription = t.ShortDescription, ImagePath = t.ImagePath })];
        });
}

internal sealed class ListSiteHostelContactsHandler(IAppDbContext db, SiteCache cache)
    : IQueryHandler<ListSiteHostelContacts, IReadOnlyList<SiteHostelContact>>
{
    // Ordered by name: legacy's Hostel Details used the site's published hostels (PropertiesByPublishToWebsiteSpec).
    public async ValueTask<IReadOnlyList<SiteHostelContact>> Handle(ListSiteHostelContacts query, CancellationToken cancellationToken) =>
        await cache.GetAsync<IReadOnlyList<SiteHostelContact>>("site:hostels:contacts", async () =>
            [.. (await db.Properties.AsNoTracking()
                .Where(p => p.PublishToSite == true)
                .OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Name, p.UrlSlug, p.ImagePath, p.TelephoneNumber, p.AddressLine1, p.AddressLine2, p.EmailAddress })
                .ToListAsync(cancellationToken))
                .Select(p => new SiteHostelContact
                {
                    Id = p.Id,
                    Name = p.Name,
                    Slug = SiteCache.Slug(p.UrlSlug, p.Name),
                    ImagePath = p.ImagePath,
                    TelephoneNumber = p.TelephoneNumber,
                    AddressLine1 = p.AddressLine1,
                    AddressLine2 = p.AddressLine2,
                    EmailAddress = p.EmailAddress,
                })]);
}

internal sealed class GetSiteHostelHandler(IAppDbContext db, SiteCache cache) : IQueryHandler<GetSiteHostel, SiteHostel>
{
    public async ValueTask<SiteHostel> Handle(GetSiteHostel query, CancellationToken cancellationToken) =>
        await cache.GetAsync($"site:hostel:{query.Slug.ToLowerInvariant()}", () => LoadAsync(query.Slug, cancellationToken));

    // Ordered by Id: legacy read these unsorted, i.e. in the tables' key order, which is what the live site shows.
    private async Task<SiteHostel> LoadAsync(string slug, CancellationToken cancellationToken)
    {
        var published = await db.Properties.AsNoTracking()
            .Where(p => p.PublishToSite == true)
            .Select(p => new { p.Id, p.Name, p.UrlSlug })
            .ToListAsync(cancellationToken);
        var match = published.FirstOrDefault(p => SiteCache.Slug(p.UrlSlug, p.Name).Equals(slug, StringComparison.OrdinalIgnoreCase))
                    ?? throw new NotFoundException("Hostel not found.");

        var hostel = await db.Properties.AsNoTracking()
            .Where(p => p.Id == match.Id)
            .Select(p => new
            {
                p.Name, p.PageTitle, p.PageSubTitle, p.H1, p.H2, p.MetaDescription, p.MetaKeywords, p.CustomSeoScripts,
                p.ImagePath, p.VideoPath, p.Description,
            })
            .SingleAsync(cancellationToken);
        var images = await db.PropertyImages.AsNoTracking().Where(i => i.PropertyId == match.Id)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => i.ImagePath).ToListAsync(cancellationToken);
        var rooms = await db.PropertyRooms.AsNoTracking().Where(r => r.PropertyId == match.Id).OrderBy(r => r.Id)
            .Select(r => new SiteRoom { Name = r.Name, Description = r.Description, ImagePath = r.ImagePath }).ToListAsync(cancellationToken);
        var facilities = await db.PropertyFacilities.AsNoTracking().Where(f => f.PropertyId == match.Id).OrderBy(f => f.Id)
            .Select(f => f.Title).ToListAsync(cancellationToken);
        var directions = await db.PropertyDirections.AsNoTracking().Where(d => d.PropertyId == match.Id).OrderBy(d => d.Id)
            .Select(d => new SiteDirection
            {
                Title = d.Title,
                Blocks = d.Content.OrderBy(c => c.Id).Select(c => new SiteDirectionBlock(c.Body, c.Style)).ToList(),
            })
            .ToListAsync(cancellationToken);

        // As legacy: the tours in the hostel's locations, or every published tour when it has none there.
        var locationIds = db.PropertyDestinationLookups.Where(l => l.PropertyId == match.Id).Select(l => l.DestinationId);
        var tours = await db.Tours.AsNoTracking()
            .Where(t => t.PublishToSite == true && db.TourDestinationLookups.Any(l => l.TourId == t.Id && locationIds.Contains(l.DestinationId)))
            .OrderBy(t => t.Id)
            .Select(t => new { t.Id, t.Name, t.UrlSlug, t.ShortDescription, t.ImagePath })
            .ToListAsync(cancellationToken);
        if (tours.Count == 0)
            tours = await db.Tours.AsNoTracking().Where(t => t.PublishToSite == true).OrderBy(t => t.Name)
                .Select(t => new { t.Id, t.Name, t.UrlSlug, t.ShortDescription, t.ImagePath })
                .ToListAsync(cancellationToken);

        // As legacy Property/Index: the browser title and head scripts come from the CMS page titled
        // "Hostels - {name}" (the hostel's own SeoPageTitle wasn't used); the description and keywords are the
        // hostel's own, never the page's, and keywords are empty (not the site's) when it has none.
        var page = await db.Pages.AsNoTracking().Where(p => p.Title == $"Hostels - {hostel.Name}").Select(p => p.Id).FirstOrDefaultAsync(cancellationToken);
        var record = page == Guid.Empty ? new PageSeo() : await SitePages.LoadAsync(db, page, cancellationToken);

        return new SiteHostel
        {
            Id = match.Id,
            Name = hostel.Name,
            Slug = SiteCache.Slug(match.UrlSlug, match.Name),
            PageTitle = hostel.PageTitle,
            PageSubTitle = hostel.PageSubTitle,
            Seo = new PageSeo
            {
                Name = hostel.Name,
                Title = record.Title,
                MetaDescription = hostel.MetaDescription,
                MetaKeywords = hostel.MetaKeywords ?? "",
                CustomSeoScripts = string.Concat(record.CustomSeoScripts, hostel.CustomSeoScripts),
                H1 = hostel.H1,
                H2 = hostel.H2,
            },
            ImagePath = hostel.ImagePath,
            VideoPath = hostel.VideoPath,
            Description = hostel.Description,
            Images = images,
            Rooms = rooms,
            Facilities = facilities,
            Directions = directions,
            Tours = [.. tours.Select(t => new SiteCard { Id = t.Id, Name = t.Name, Slug = SiteCache.Slug(t.UrlSlug, t.Name), ShortDescription = t.ShortDescription, ImagePath = t.ImagePath })],
        };
    }
}

internal sealed class ListSiteEventOccurrencesHandler(IAppDbContext db) : IQueryHandler<ListSiteEventOccurrences, IReadOnlyList<SiteEventOccurrence>>
{
    public async ValueTask<IReadOnlyList<SiteEventOccurrence>> Handle(ListSiteEventOccurrences query, CancellationToken cancellationToken)
    {
        var events = db.Events.Where(e => e.PublishToSite == true);
        if (query.HostelId is { } hostelId)
            events = events.Where(e => e.AllProperties == true || e.EventPropertyLookups.Any(l => l.PropertyId == hostelId));
        if (query.EventId is { } eventId)
            events = events.Where(e => e.Id == eventId);

        var occurrences = await Events.EventOccurrences.ExpandAsync(db, events, query.From, query.To, cancellationToken);
        var eventIds = occurrences.Select(o => o.EventId).Distinct().ToList();
        var tourOf = await db.Events.AsNoTracking().Where(e => eventIds.Contains(e.Id) && e.TourId != null)
            .ToDictionaryAsync(e => e.Id, e => e.TourId, cancellationToken);
        var booking = await EventTours.LoadAsync(db, tourOf.Values, query.From.Date, query.To.Date.AddDays(1), cancellationToken);

        return [.. occurrences
            .Select(o => new SiteEventOccurrence
            {
                EventId = o.EventId,
                Name = o.Name,
                Start = o.Start,
                End = o.End,
                AllDay = o.AllDay,
                Colour = o.Colour,
                CategoryName = o.CategoryName,
                ShortDescription = o.ShortDescription,
                ImagePath = o.ImagePath,
                Url = $"/events/{Slugs.From(o.Name)}",
                Booking = booking.For(tourOf.GetValueOrDefault(o.EventId)),
                Date = tourOf.GetValueOrDefault(o.EventId) is { } tour ? booking.Date(tour, o.Start, o.End) : null,
            })];
    }
}

internal sealed class GetHostelBookingEngineHandler(IAppDbContext db, SiteCache cache) : IQueryHandler<GetHostelBookingEngine, HostelBookingEngine>
{
    public async ValueTask<HostelBookingEngine> Handle(GetHostelBookingEngine query, CancellationToken cancellationToken) =>
        await cache.GetAsync($"site:hostel-booking:{query.Slug.ToLowerInvariant()}", async () =>
        {
            var hostels = await db.Properties.AsNoTracking()
                .Where(p => p.PublishToSite == true && p.CloudbedsKey != null)
                .Select(p => new { p.Name, p.UrlSlug, p.CloudbedsKey, p.ImagePath })
                .ToListAsync(cancellationToken);
            var hostel = SiteCache.FindForBooking(hostels, h => h.UrlSlug, h => h.Name, query.Slug)
                         ?? throw new NotFoundException("Hostel not found.");
            return new HostelBookingEngine(hostel.Name, SiteCache.Slug(hostel.UrlSlug, hostel.Name), hostel.CloudbedsKey!, hostel.ImagePath);
        });
}

internal sealed class GetHostelBookingLinkHandler(IAppDbContext db, ICurrentUser user) : IQueryHandler<GetHostelBookingLink, string>
{
    public async ValueTask<string> Handle(GetHostelBookingLink query, CancellationToken cancellationToken)
    {
        var hostels = await db.Properties.AsNoTracking()
            .Where(p => p.PublishToSite == true && p.CloudbedsKey != null)
            .Select(p => new { p.Name, p.UrlSlug, p.CloudbedsKey })
            .ToListAsync(cancellationToken);
        var hostel = SiteCache.FindForBooking(hostels, h => h.UrlSlug, h => h.Name, query.Slug)
                     ?? throw new NotFoundException("Hostel not found.");

        var link = new StringBuilder($"https://hotels.cloudbeds.com/reservation/{hostel.CloudbedsKey}");

        // The signed-in guest's details, so they needn't type them (legacy read them from a user id in the URL).
        if (user.UserId is { } userId)
        {
            var guest = await db.Users.AsNoTracking().Where(u => u.Id == userId.ToString())
                .Select(u => new { u.FirstName, u.LastName, u.Email, u.Nationality, u.PhoneNumber })
                .FirstOrDefaultAsync(cancellationToken);
            if (guest is not null)
            {
                var details = new[]
                    {
                        ("firstName", guest.FirstName), ("lastName", guest.LastName), ("email", Identity.PlaceholderEmails.Visible(guest.Email)),
                        ("country", guest.Nationality), ("phone", guest.PhoneNumber),
                    }
                    .Where(d => !string.IsNullOrEmpty(d.Item2))
                    .Select(d => $"{d.Item1}={Uri.EscapeDataString(d.Item2!)}");
                link.Append('?').AppendJoin('&', details);
            }
        }

        // Cloudbeds reads the dates from the fragment.
        if (query.CheckIn is not null || query.CheckOut is not null)
        {
            link.Append('#');
            if (query.CheckIn is { } checkIn)
                link.Append($"&checkin={checkIn:yyyy-MM-dd}");
            if (query.CheckOut is { } checkOut)
                link.Append($"&checkout={checkOut:yyyy-MM-dd}");
        }

        return link.ToString();
    }
}

internal static class SitePages
{
    /// <summary>A page's SEO and headings; an empty record when the row is missing (legacy threw instead).</summary>
    public static async Task<PageSeo> LoadAsync(IAppDbContext db, DefaultIdType id, CancellationToken cancellationToken) =>
        await db.Pages.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new PageSeo
            {
                Name = p.Title,
                Title = p.SeoPageTitle,
                MetaDescription = p.MetaDescription,
                MetaKeywords = p.MetaKeywords,
                CustomSeoScripts = p.CustomSeoScripts,
                H1 = p.H1,
                H2 = p.H2,
                H3 = p.H3,
            })
            .FirstOrDefaultAsync(cancellationToken) ?? new PageSeo();
}

internal sealed class GetSitePageHandler(IAppDbContext db, SiteCache cache) : IQueryHandler<GetSitePage, PageSeo>
{
    public async ValueTask<PageSeo> Handle(GetSitePage query, CancellationToken cancellationToken) =>
        await cache.GetAsync($"site:page:{query.Id}", () => SitePages.LoadAsync(db, query.Id, cancellationToken));
}
