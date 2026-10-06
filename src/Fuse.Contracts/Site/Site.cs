namespace Fuse.Contracts.Site;

// The public website. Everything here is anonymous and cached on the server for a few minutes, as
// legacy cached its published hostels and tours.

/// <summary>A published hostel or tour, for menus and links (<c>/hostels/{Slug}</c>, <c>/tours/{Slug}</c>).</summary>
public sealed record SiteLink
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
}

/// <summary>The site-wide SEO defaults (legacy <c>FuseSettings.MetaData</c> and the <c>Seo</c> row).</summary>
public sealed record SiteSeo
{
    public string SiteName { get; init; } = "";
    /// <summary>The canonical origin, e.g. https://fusehostelsandtravel.com.</summary>
    public string WebsiteUrl { get; init; } = "";
    public string? MetaDescription { get; init; }
    public string? MetaKeywords { get; init; }
    public string? MetaImageUrl { get; init; }
    public string? GoogleSiteVerification { get; init; }
    /// <summary>False outside production: pages ask search engines not to index them.</summary>
    public bool Indexable { get; init; }
    /// <summary>Structured data kept in the database (raw HTML, usually JSON-LD script tags).</summary>
    public string? Breadcrumbs { get; init; }
    public string? SearchAction { get; init; }
}

/// <summary>What every public page's header, footer and booking form need.</summary>
public sealed record SiteChrome
{
    public IReadOnlyList<SiteLink> Hostels { get; init; } = [];
    public IReadOnlyList<SiteLink> Tours { get; init; } = [];
    /// <summary>Published hostels that take online bookings (they have a Cloudbeds key).</summary>
    public IReadOnlyList<SiteLink> BookableHostels { get; init; } = [];
    /// <summary>The reCAPTCHA site key for the enquiry forms.</summary>
    public string CaptchaSiteKey { get; init; } = "";
    /// <summary>Google Ads' conversion for opening the hostel booking page (sent only where the tags load: production).</summary>
    public string? HostelBookingConversion { get; init; }
    public SiteSeo Seo { get; init; } = new();
}

[Get("site/chrome"), Public]
public sealed record GetSiteChrome : IQuery<SiteChrome>;

/// <summary>A CMS page's SEO and headings (the <c>Pages</c> row).</summary>
public sealed record PageSeo
{
    /// <summary>The page's name in the admin ("Our Story").</summary>
    public string? Name { get; init; }
    /// <summary>The SEO title, when set it replaces the whole browser title.</summary>
    public string? Title { get; init; }
    public string? MetaDescription { get; init; }
    public string? MetaKeywords { get; init; }
    public string? CustomSeoScripts { get; init; }
    public string? H1 { get; init; }
    public string? H2 { get; init; }
    public string? H3 { get; init; }
}

/// <summary>A published hostel or tour as the home page shows it.</summary>
public sealed record SiteCard
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
    /// <summary>Rich text (HTML).</summary>
    public string? ShortDescription { get; init; }
    public string? ImagePath { get; init; }
}

public sealed record SiteEvent
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Its page: /events/{slug} (legacy's name made URL-friendly).</summary>
    public string Slug { get; init; } = "";
    /// <summary>Rich text (HTML).</summary>
    public string? ShortDescription { get; init; }
    public string? ImagePath { get; init; }
    /// <summary>"r, g, b": the card's tint.</summary>
    public string? BackgroundColor { get; init; }
    public string? CategoryName { get; init; }
    /// <summary>Hex: the event's own colour, else its category's (as the calendars).</summary>
    public string? Colour { get; init; }
    /// <summary>"Every Mon &amp; Wed"; null for a one-off.</summary>
    public string? Repeats { get; init; }
    public bool AllDay { get; init; }
    /// <summary>
    /// When it's on over its next few days (cancelled ones left out), soonest first: one per time of day it's on,
    /// so several a day for an event with <see cref="Times"/>; none once it's over.
    /// </summary>
    public IReadOnlyList<SiteEventDate> Upcoming { get; init; } = [];
    /// <summary>The times of day it's on (e.g. Happy Hour at 12pm, 5pm and 7pm); none = its own start time.</summary>
    public IReadOnlyList<SiteEventTime> Times { get; init; } = [];
    /// <summary>On at every hostel; otherwise at <see cref="Hostels"/>.</summary>
    public bool AllHostels { get; init; }
    public IReadOnlyList<SiteLink> Hostels { get; init; } = [];
    /// <summary>A Facebook event, ticket page...</summary>
    public string? ExternalUrl { get; init; }
    /// <summary>Booked as a tour (its dates carry the departures); null when it isn't.</summary>
    public SiteEventBooking? Booking { get; init; }
}

/// <summary>
/// The tour an event is booked as: its page, and the lowest price the website sells it at (none: on request).
/// <see cref="Online"/>: it can be booked on the site (a web price and departures to come); otherwise Book opens an
/// enquiry for the tour.
/// </summary>
public sealed record SiteEventBooking(string TourSlug, decimal? Price, DefaultIdType TourId = default, string TourName = "", bool Online = true);

/// <summary>
/// One time the event's on. For an event booked as a tour: that day's departure (the one starting nearest the event), and
/// whether it's full.
/// </summary>
public sealed record SiteEventDate(DateTime Start, DateTime End, DefaultIdType? DepartureId = null, bool SoldOut = false, decimal? Price = null);

public sealed record SiteEventTime(TimeSpan Start, TimeSpan? End);

/// <summary>An event's own page: the event (its next eight days) and its full description (HTML).</summary>
public sealed record SiteEventDetails(SiteEvent Event, string? Description);

/// <summary>A published event by its page's slug (/events/{Slug}).</summary>
[Get("site/events/{Slug}"), Public]
public sealed record GetSiteEvent(string Slug) : IQuery<SiteEventDetails>;

/// <summary>A hostel's contact details (legacy "HOSTEL DETAILS", on the events and contact pages).</summary>
public sealed record SiteHostelContact
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Its page: /hostels/{slug}.</summary>
    public string Slug { get; init; } = "";
    public string? ImagePath { get; init; }
    public string? TelephoneNumber { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? EmailAddress { get; init; }
}

/// <summary>Published hostels' contact details, in the order the site lists them.</summary>
[Get("site/hostels/contacts"), Public]
public sealed record ListSiteHostelContacts : IQuery<IReadOnlyList<SiteHostelContact>>;

/// <summary>Published hostels, by name.</summary>
[Get("site/hostels"), Public]
public sealed record ListSiteHostels : IQuery<IReadOnlyList<SiteCard>>;

/// <summary>Published tours, by name.</summary>
[Get("site/tours"), Public]
public sealed record ListSiteTours : IQuery<IReadOnlyList<SiteCard>>;

/// <summary>Published events, newest first; the home page shows only the featured ones.</summary>
[Get("site/events"), Public]
public sealed record ListSiteEvents(bool FeaturedOnly = false) : IQuery<IReadOnlyList<SiteEvent>>;

/// <summary>A hostel's room, as its page shows it.</summary>
public sealed record SiteRoom
{
    public string Name { get; init; } = "";
    /// <summary>Rich text (HTML).</summary>
    public string? Description { get; init; }
    public string? ImagePath { get; init; }
}

/// <summary>A tab of a hostel's "Getting to" directions: blocks of rich text side by side (text, then a map).</summary>
public sealed record SiteDirection
{
    public string Title { get; init; } = "";
    public IReadOnlyList<SiteDirectionBlock> Blocks { get; init; } = [];
}

public sealed record SiteDirectionBlock(string Html, string? Style);

/// <summary>Everything a published hostel's page shows.</summary>
public sealed record SiteHostel
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
    /// <summary>"Hoi An Hostel": names the experiences and directions sections.</summary>
    public string? PageTitle { get; init; }
    /// <summary>"Old Town".</summary>
    public string? PageSubTitle { get; init; }
    public PageSeo Seo { get; init; } = new();
    public string? ImagePath { get; init; }
    public string? VideoPath { get; init; }
    /// <summary>Rich text (HTML).</summary>
    public string? Description { get; init; }
    /// <summary>The header slideshow, in order.</summary>
    public IReadOnlyList<string> Images { get; init; } = [];
    public IReadOnlyList<SiteRoom> Rooms { get; init; } = [];
    public IReadOnlyList<string> Facilities { get; init; } = [];
    public IReadOnlyList<SiteDirection> Directions { get; init; } = [];
    /// <summary>Published tours in the hostel's locations (all published tours if none are).</summary>
    public IReadOnlyList<SiteCard> Tours { get; init; } = [];
}

/// <summary>A published hostel by its URL slug (NotFoundException when there's no such hostel on the site).</summary>
[Get("site/hostels/{Slug}"), Public]
public sealed record GetSiteHostel(string Slug) : IQuery<SiteHostel>;

/// <summary>
/// Where to book a stay: the hostel's Cloudbeds booking page, with the dates (and the signed-in guest's details)
/// filled in (legacy /hostel-booking redirected there).
/// </summary>
[Get("site/hostels/{Slug}/booking-link"), Public]
public sealed record GetHostelBookingLink(string Slug, DateOnly? CheckIn = null, DateOnly? CheckOut = null) : IQuery<string>;

/// <summary>A bookable hostel's Cloudbeds booking engine: its 6-character code (as in hotels.cloudbeds.com/reservation/{code}).</summary>
public sealed record HostelBookingEngine(string Name, string Slug, string PropertyCode, string? ImagePath = null);

/// <summary>What the embedded booking engine page needs (Site:HostelBooking = Embed).</summary>
[Get("site/hostels/{Slug}/booking-engine"), Public]
public sealed record GetHostelBookingEngine(string Slug) : IQuery<HostelBookingEngine>;

/// <summary>One date of a published event, as the site's calendars show it.</summary>
public sealed record SiteEventOccurrence
{
    public DefaultIdType EventId { get; init; }
    public string Name { get; init; } = "";
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    public bool AllDay { get; init; }
    public string? Colour { get; init; }
    public string? CategoryName { get; init; }
    /// <summary>Rich text (HTML).</summary>
    public string? ShortDescription { get; init; }
    public string? ImagePath { get; init; }
    /// <summary>The event's own page, e.g. /events/pub-quiz.</summary>
    public string Url { get; init; } = "";
    /// <summary>Booked as a tour: the tour, and this date's departure (<see cref="Date"/>).</summary>
    public SiteEventBooking? Booking { get; init; }
    public SiteEventDate? Date { get; init; }
    /// <summary>On at every hostel; otherwise at <see cref="Hostels"/> (their names, A–Z).</summary>
    public bool AllHostels { get; init; }
    public IReadOnlyList<string> Hostels { get; init; } = [];
}

/// <summary>
/// Published events' dates in a window (at most about six months), for a hostel (its own events and those at
/// every hostel) or for all hostels.
/// </summary>
[Post("site/events/occurrences"), Public]
public sealed record ListSiteEventOccurrences(DateTime From, DateTime To, DefaultIdType? HostelId = null, DefaultIdType? EventId = null)
    : IQuery<IReadOnlyList<SiteEventOccurrence>>;

/// <summary>A CMS page's SEO and headings (every public page has a record).</summary>
[Get("site/pages/{Id}"), Public]
public sealed record GetSitePage(DefaultIdType Id) : IQuery<PageSeo>;

/// <summary>A page in the sitemap (legacy /sitemap.xml), its path relative to the site's root ("" for home).</summary>
public sealed record SiteMapEntry
{
    public string Path { get; init; } = "";
    /// <summary>None when there's nothing to date it by (e.g. no published tours yet).</summary>
    public DateTime? LastModified { get; init; }
    /// <summary>"monthly", "yearly"…</summary>
    public string ChangeFrequency { get; init; } = "";
    public decimal Priority { get; init; }
    public SiteMapVideo? Video { get; init; }
    public SiteMapNews? News { get; init; }
}

public sealed record SiteMapVideo(string Title, string Description, string ThumbnailUrl, string ContentUrl);

public sealed record SiteMapNews(string Title, DateTime Published);

/// <summary>Everything the sitemap lists, as legacy's: home, hostels, tours, travel guides and the fixed pages.</summary>
[Get("site/sitemap"), Public]
public sealed record GetSiteMap : IQuery<IReadOnlyList<SiteMapEntry>>;

/// <summary>An old address and where it now lives (Page Management's redirects).</summary>
public sealed record SiteRedirect(string Url, string RedirectUrl);

/// <summary>The redirects the site applies (as legacy, permanently) before anything else.</summary>
[Get("site/redirects"), Public]
public sealed record ListSiteRedirects : IQuery<IReadOnlyList<SiteRedirect>>;
