using Fuse.Shared.Text;
using Fuse.Application.Common.Time;
using Fuse.Contracts.Site;

namespace Fuse.Application.Features.Site;

// Legacy TourPageModel: a published tour found by its slug, with the prices the website may sell (published to
// the website, not in-house only) and the departures at those prices from now on.

internal sealed class GetSiteTourHandler(IAppDbContext db, SiteCache cache) : IQueryHandler<GetSiteTour, SiteTour>
{
    public async ValueTask<SiteTour> Handle(GetSiteTour query, CancellationToken cancellationToken)
    {
        var staff = await StaffReferrals.FindAsync(db, query.StaffId, cancellationToken);
        var tour = await cache.GetAsync($"site:tour:{query.Slug.ToLowerInvariant()}:{(staff is null ? "web" : "staff")}",
            () => LoadAsync(query.Slug, staff is not null, cancellationToken));
        return staff is { } s ? tour with { StaffName = s.FirstName } : tour;
    }

    private async Task<SiteTour> LoadAsync(string slug, bool staff, CancellationToken cancellationToken)
    {
        var bookable = await db.Tours.AsNoTracking().Where(SiteBookableTours.Rule(db, staff))
            .Select(t => new { t.Id, t.Name, t.UrlSlug, t.PublishToSite }).ToListAsync(cancellationToken);
        // Staff codes printed from legacy used the tour's name made URL-friendly, even where the tour has its own slug.
        var match = bookable.FirstOrDefault(t => SiteCache.Slug(t.UrlSlug, t.Name).Equals(slug, StringComparison.OrdinalIgnoreCase))
                    ?? (staff ? bookable.FirstOrDefault(t => Slugs.From(t.Name).Equals(slug, StringComparison.OrdinalIgnoreCase)) : null)
                    ?? throw new NotFoundException("Tour not found.");

        var tour = await db.Tours.AsNoTracking().Where(t => t.Id == match.Id)
            .Select(t => new
            {
                t.Id, t.Name, t.UrlSlug, t.ImagePath, t.Description, t.H1, t.H2, t.DayDuration, t.NightDuration, t.HourDuration,
                t.MetaDescription, t.MetaKeywords, t.CustomSeoScripts,
            })
            .SingleAsync(cancellationToken);
        var prices = await db.TourPrices.AsNoTracking()
            .Where(p => p.TourId == tour.Id).Where(SiteBookableTours.Price(staff))
            .OrderBy(p => p.Id)
            .Select(p => new SiteTourPrice(p.Id, p.Title, p.Price))
            .ToListAsync(cancellationToken);
        var pickups = await db.TourPickupLocations.AsNoTracking()
            .Where(l => l.TourId == tour.Id)
            .Select(l => l.Property.Name)
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);
        // Legacy looked for a CMS page titled "Tours - {name}" for the browser title.
        var page = await db.Pages.AsNoTracking().Where(p => p.Title == $"Tours - {tour.Name}").Select(p => p.Id).FirstOrDefaultAsync(cancellationToken);
        var record = page == Guid.Empty ? new PageSeo() : await SitePages.LoadAsync(db, page, cancellationToken);

        return new SiteTour
        {
            Id = tour.Id,
            Name = tour.Name,
            Slug = SiteCache.Slug(tour.UrlSlug, tour.Name),
            Listed = match.PublishToSite == true,
            ImagePath = tour.ImagePath,
            Heading = (string.IsNullOrEmpty(tour.H1) ? tour.Name : tour.H1).ToUpperInvariant(),
            SubHeading = string.IsNullOrEmpty(tour.H2) ? null : tour.H2,
            Description = tour.Description,
            Duration = Duration(tour.DayDuration, tour.NightDuration, tour.HourDuration),
            Prices = prices,
            Pickups = pickups,
            Seo = record with
            {
                MetaDescription = tour.MetaDescription ?? record.MetaDescription,
                MetaKeywords = tour.MetaKeywords ?? record.MetaKeywords,
                CustomSeoScripts = string.Concat(record.CustomSeoScripts, tour.CustomSeoScripts),
            },
        };
    }

    /// <summary>Legacy's wording: "2 Days 1 Night", "4 Hours" (only a day count puts a space before the hours).</summary>
    private static string Duration(string? days, string? nights, string? hours)
    {
        var text = "";
        if (!string.IsNullOrEmpty(days))
            text += $"{days} Day{(days != "1" ? "s" : "")}";
        if (!string.IsNullOrEmpty(nights))
            text += $"{(text.Length > 0 ? " " : "")}{nights} Night{(nights != "1" ? "s" : "")}";
        if (!string.IsNullOrEmpty(hours))
            text += $"{(!string.IsNullOrEmpty(days) ? " " : "")}{hours} Hour{(hours != "1" ? "s" : "")}";
        return text;
    }
}

internal sealed class ListSiteTourDeparturesHandler(IAppDbContext db, IBusinessClock clock)
    : IQueryHandler<ListSiteTourDepartures, IReadOnlyList<SiteDeparture>>
{
    /// <summary>Plenty for the booking calendar, and a bound on what one request can pull.</summary>
    private const int Max = 1000;

    public async ValueTask<IReadOnlyList<SiteDeparture>> Handle(ListSiteTourDepartures query, CancellationToken cancellationToken)
    {
        var now = clock.Now;
        var staff = await StaffReferrals.FindAsync(db, query.StaffId, cancellationToken) is not null;
        var prices = db.TourPrices.Where(SiteBookableTours.Price(staff)).Select(p => (DefaultIdType?)p.Id);
        return await db.TourDates.AsNoTracking()
            .Where(d => d.TourId == query.TourId && d.StartDate > now
                        && db.Tours.Where(SiteBookableTours.Rule(db, staff)).Any(t => t.Id == d.TourId)
                        && prices.Contains(d.TourPriceId))
            .OrderBy(d => d.StartDate)
            .Take(Max)
            .Select(d => new SiteDeparture(d.Id, d.StartDate, d.EndDate, d.TourPrice.Title, d.PriceOverride ?? d.TourPrice.Price, d.AvailableSpaces))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>
/// Tours the website can book: published ones, and hidden ones (not listed or in the sitemap) that a published event is
/// booked as, like legacy's staff booking page for tours not on the site.
/// </summary>
internal static class SiteBookableTours
{
    public static System.Linq.Expressions.Expression<Func<Domain.Catalog.Tours.Tour, bool>> Rule(IAppDbContext db) =>
        t => t.PublishToSite == true || db.Events.Any(e => e.TourId == t.Id && e.PublishToSite == true);

    /// <summary>Through a staff member's QR code: also the tours shown only by QR code (legacy's staff booking page).</summary>
    public static System.Linq.Expressions.Expression<Func<Domain.Catalog.Tours.Tour, bool>> Rule(IAppDbContext db, bool staff) =>
        staff
            ? t => t.PublishToSite == true || t.ShowBookingQRCode == true || db.Events.Any(e => e.TourId == t.Id && e.PublishToSite == true)
            : Rule(db);

    /// <summary>
    /// A price the website sells at: published to it and not in-house only. Through a staff QR code, unpublished prices too,
    /// but never in-house ones (as legacy's staff booking page).
    /// </summary>
    public static System.Linq.Expressions.Expression<Func<Domain.Catalog.Tours.TourPrice, bool>> Price(bool staff) =>
        staff ? p => p.InHouseOnly != true : p => p.PublishToWebsite == true && p.InHouseOnly != true;
}

/// <summary>
/// The staff member behind a booking QR code: an active back-office user (not a supplier), else none (the link then
/// works as an ordinary tour page).
/// </summary>
internal static class StaffReferrals
{
    public static async Task<(DefaultIdType Id, string FirstName)?> FindAsync(IAppDbContext db, DefaultIdType? staffId, CancellationToken cancellationToken)
    {
        if (staffId is not { } id)
            return null;
        var userId = id.ToString();
        var staffRoles = FuseRoles.StaffRoles.Where(r => r != FuseRoles.Supplier).ToList();
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive
                        && db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && staffRoles.Contains(r.Name!))))
            .Select(u => new { u.FirstName })
            .FirstOrDefaultAsync(cancellationToken);
        return user is null ? null : (id, user.FirstName ?? "");
    }

}
