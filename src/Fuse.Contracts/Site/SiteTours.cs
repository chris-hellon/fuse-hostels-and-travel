namespace Fuse.Contracts.Site;

/// <summary>A published tour's page (legacy Tour/Index): what it is, how long, its web prices and pick-up points.</summary>
public sealed record SiteTour
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
    /// <summary>
    /// Published (in the tours list and sitemap). A hidden tour that an event is booked as can still be booked from its
    /// page, which search engines are asked not to index.
    /// </summary>
    public bool Listed { get; init; } = true;
    public string? ImagePath { get; init; }
    /// <summary>The page heading (legacy: the tour's H1, else its name) and optional subheading.</summary>
    public string Heading { get; init; } = "";
    public string? SubHeading { get; init; }
    /// <summary>Rich text (HTML).</summary>
    public string? Description { get; init; }
    /// <summary>"2 Days 1 Night", "4 Hours" (legacy's wording).</summary>
    public string Duration { get; init; } = "";
    /// <summary>Prices bookable on the website.</summary>
    public IReadOnlyList<SiteTourPrice> Prices { get; init; } = [];
    /// <summary>Hostels it picks up from (their names), in name order.</summary>
    public IReadOnlyList<string> Pickups { get; init; } = [];
    /// <summary>Opened from a staff member's booking QR code: their first name (the booking is credited to them).</summary>
    public string? StaffName { get; init; }
    /// <summary>SEO: the tour's own values, over its "Tours - {name}" page record's.</summary>
    public PageSeo Seo { get; init; } = new();
}

public sealed record SiteTourPrice(DefaultIdType Id, string Title, decimal Price);

[Get("site/tours/{Slug}"), Public]
public sealed record GetSiteTour(string Slug, DefaultIdType? StaffId = null) : IQuery<SiteTour>;

/// <summary>A departure visitors can book: when, its price, and how many places are left (0 = sold out).</summary>
public sealed record SiteDeparture(DefaultIdType Id, DateTime Start, DateTime End, string PriceTitle, decimal Price, int AvailableSpaces);

/// <summary>A published tour's departures still to come at website prices, soonest first (live: never cached).</summary>
[Get("site/tours/{TourId}/departures"), Public]
public sealed record ListSiteTourDepartures(DefaultIdType TourId, DefaultIdType? StaffId = null) : IQuery<IReadOnlyList<SiteDeparture>>;

// Staff booking QR codes (legacy's /tours/staff-booking/{tour}/{staff}): a staff member's code opens a tour's page with
// StaffId, which (for a real, active staff member, not a supplier) also offers tours shown only by QR code and in-house
// prices, as legacy did, and credits the booking to them.
