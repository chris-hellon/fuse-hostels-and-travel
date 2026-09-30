using Fuse.Contracts.Site;
using Fuse.Shared.Text;

namespace Fuse.Application.Features.Site;

// Legacy's /sitemap.xml (SharedRCL Pages/Sitemap) and the SEO redirects middleware, as data; the web host writes
// the XML and applies the redirects. Legacy also listed /hostel-booking, which isn't a page (booking is
// /hostel-booking/{hostel}, a redirect to Cloudbeds), so it's left out; it didn't list events' own pages, which it
// now does.

internal sealed class GetSiteMapHandler(IAppDbContext db, SiteCache cache) : IQueryHandler<GetSiteMap, IReadOnlyList<SiteMapEntry>>
{
    private const string Monthly = "monthly", Yearly = "yearly", Weekly = "weekly";

    // The home page's video, as legacy's sitemap listed it.
    private static readonly SiteMapVideo HomeVideo = new("Home Page Video", "",
        "https://travaloudcdn.azureedge.net/fuse/assets/images/POOLPARTY_23-03-22-12-min.jpg?w=300",
        "https://travaloud.azureedge.net/fuse/assets/images/0b47297a-3910-4e83-8f83-a94e35996c09.mp4");

    public async ValueTask<IReadOnlyList<SiteMapEntry>> Handle(GetSiteMap query, CancellationToken cancellationToken) =>
        await cache.GetAsync<IReadOnlyList<SiteMapEntry>>("site:sitemap", () => LoadAsync(cancellationToken));

    private async Task<IReadOnlyList<SiteMapEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        var hostels = await db.Properties.AsNoTracking().Where(p => p.PublishToSite == true)
            .Select(p => new { p.Name, p.UrlSlug, p.ImagePath, p.VideoPath, Modified = p.LastModifiedOn ?? p.CreatedOn })
            .OrderByDescending(p => p.Modified).ToListAsync(cancellationToken);
        var tours = await db.Tours.AsNoTracking().Where(t => t.PublishToSite == true)
            .Select(t => new { t.Name, t.UrlSlug, Modified = t.LastModifiedOn ?? t.CreatedOn })
            .OrderByDescending(t => t.Modified).ToListAsync(cancellationToken);
        var events = await db.Events.AsNoTracking().Where(e => e.PublishToSite == true)
            .Select(e => new { e.Name, Modified = e.LastModifiedOn ?? e.CreatedOn })
            .OrderByDescending(e => e.Modified).ToListAsync(cancellationToken);
        var guides = await db.TravelGuides.AsNoTracking()
            .Select(g => new { g.Title, g.UrlFriendlyTitle, g.CreatedOn, Modified = g.LastModifiedOn ?? g.CreatedOn })
            .ToListAsync(cancellationToken);

        // As legacy: home dated by the latest tour change, the hostels and tours lists by the latest hostel change.
        DateTime? latestTour = tours.Count > 0 ? tours[0].Modified : null;
        DateTime? latestHostel = hostels.Count > 0 ? hostels[0].Modified : null;

        List<SiteMapEntry> entries =
        [
            new() { Path = "", LastModified = latestTour, ChangeFrequency = Monthly, Priority = 0.5m, Video = HomeVideo },
            Page("hostels", latestHostel, Yearly),
            .. hostels.Select(h => Page($"hostels/{SiteCache.Slug(h.UrlSlug, h.Name)}", h.Modified, Yearly) with
            {
                Video = string.IsNullOrEmpty(h.VideoPath) ? null
                    : new SiteMapVideo($"{h.Name} Video.", $"{h.Name} Hostel Welcome Video.", h.ImagePath ?? "", h.VideoPath),
            }),
            Page("tours", latestHostel, Monthly),
            .. tours.Select(t => Page($"tours/{SiteCache.Slug(t.UrlSlug, t.Name)}", t.Modified, Monthly)),
            Page("our-story", new DateTime(2024, 2, 12, 10, 23, 0), Yearly),
            Page("events", new DateTime(2024, 5, 7, 14, 24, 0), Monthly),
            // Not in legacy's sitemap: each published event's own page (one per address, if two share a name).
            .. events.Where(e => !string.IsNullOrEmpty(e.Name)).DistinctBy(e => Slugs.From(e.Name)).Select(e => Page($"events/{Slugs.From(e.Name)}", e.Modified, Monthly)),
            // Legacy listed /get-in-touch, which 404ed; the contact page.
            Page("contact", new DateTime(2023, 10, 7, 12, 33, 0), Yearly),
            // Legacy listed these under /travel-guides, which only redirects (its guides' addresses 404ed); the real ones.
            Page("blogs-and-tips", new DateTime(2024, 4, 5, 13, 24, 6), Weekly),
            .. guides.Select(g => Page($"blogs-and-tips/{SiteTravelGuides.Slug(g.UrlFriendlyTitle, g.Title)}", g.Modified, Yearly) with { News = new SiteMapNews(g.Title, g.CreatedOn) }),
            Page("join-our-crew", new DateTime(2024, 2, 12, 9, 52, 45), Monthly),
            Page("terms-and-conditions", new DateTime(2023, 6, 1, 14, 12, 12), Yearly),
            Page("cookie-policy", new DateTime(2023, 6, 1, 13, 58, 0), Yearly),
            // Not in legacy's sitemap, though the footer links it.
            Page("privacy-policy", new DateTime(2023, 6, 1, 13, 58, 0), Yearly),
        ];
        return entries;
    }

    private static SiteMapEntry Page(string path, DateTime? modified, string frequency) =>
        new() { Path = path, LastModified = modified, ChangeFrequency = frequency, Priority = 0.1m };
}

internal sealed class ListSiteRedirectsHandler(IAppDbContext db, SiteCache cache) : IQueryHandler<ListSiteRedirects, IReadOnlyList<SiteRedirect>>
{
    public async ValueTask<IReadOnlyList<SiteRedirect>> Handle(ListSiteRedirects query, CancellationToken cancellationToken) =>
        await cache.GetAsync<IReadOnlyList<SiteRedirect>>("site:redirects", async () =>
            [.. (await db.SeoRedirects.AsNoTracking().Select(r => new { r.Url, r.RedirectUrl }).ToListAsync(cancellationToken))
                .Where(r => !string.IsNullOrWhiteSpace(r.Url) && !string.IsNullOrWhiteSpace(r.RedirectUrl))
                .Select(r => new SiteRedirect(r.Url.Trim(), r.RedirectUrl.Trim()))]);
}
