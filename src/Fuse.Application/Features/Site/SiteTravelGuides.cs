using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Fuse.Contracts.Site;

namespace Fuse.Application.Features.Site;

// Legacy TravelGuides and TravelGuide pages. Every guide is public (they have no publish switch), newest first.

internal sealed class ListSiteTravelGuidesHandler(IAppDbContext db, SiteCache cache) : IQueryHandler<ListSiteTravelGuides, SiteTravelGuidePage>
{
    /// <summary>Legacy's page size: the latest large, then two rows of three.</summary>
    public const int PageSize = 7;

    public async ValueTask<SiteTravelGuidePage> Handle(ListSiteTravelGuides query, CancellationToken cancellationToken) =>
        await cache.GetAsync($"site:guides:{query.Page}", async () =>
        {
            var total = await db.TravelGuides.CountAsync(cancellationToken);
            var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            // Past the last page: none (the page shows not found), rather than repeating the last.
            var page = Math.Max(1, query.Page);
            var guides = await SiteTravelGuides.Cards(db.TravelGuides.AsNoTracking().OrderByDescending(g => g.CreatedOn).ThenBy(g => g.Id)
                .Skip((page - 1) * PageSize).Take(PageSize), cancellationToken);
            return new SiteTravelGuidePage(guides, page, pages);
        });
}

internal sealed class GetSiteTravelGuideHandler(IAppDbContext db, SiteCache cache) : IQueryHandler<GetSiteTravelGuide, SiteTravelGuide>
{
    public async ValueTask<SiteTravelGuide> Handle(GetSiteTravelGuide query, CancellationToken cancellationToken) =>
        await cache.GetAsync($"site:guide:{query.Slug.ToLowerInvariant()}", () => LoadAsync(query.Slug, cancellationToken));

    private async Task<SiteTravelGuide> LoadAsync(string slug, CancellationToken cancellationToken)
    {
        var all = await db.TravelGuides.AsNoTracking().Select(g => new { g.Id, g.Title, g.UrlFriendlyTitle }).ToListAsync(cancellationToken);
        var match = all.FirstOrDefault(g => SiteTravelGuides.Slug(g.UrlFriendlyTitle, g.Title).Equals(slug, StringComparison.OrdinalIgnoreCase))
                    ?? throw new NotFoundException("Travel guide not found.");

        var guide = await db.TravelGuides.AsNoTracking().Where(g => g.Id == match.Id)
            .Select(g => new
            {
                g.Id, g.Title, g.SubTitle, g.UrlFriendlyTitle, g.Description, g.ImagePath, g.CreatedOn, g.CreatedBy, g.MetaKeywords, g.MetaDescription,
                Gallery = g.TravelGuideGalleryImages!.OrderBy(i => i.SortOrder).Select(i => i.ImagePath).ToList(),
            })
            .SingleAsync(cancellationToken);
        var createdBy = guide.CreatedBy.ToString();
        var author = await db.Users.AsNoTracking().Where(u => u.Id == createdBy)
            .Select(u => new { u.FirstName, u.LastName }).FirstOrDefaultAsync(cancellationToken);

        // As legacy: three others, picked at random.
        var others = all.Where(g => g.Id != guide.Id).Select(g => g.Id).OrderBy(_ => Random.Shared.Next()).Take(3).ToList();
        var related = await SiteTravelGuides.Cards(db.TravelGuides.AsNoTracking().Where(g => others.Contains(g.Id)), cancellationToken);

        var subTitle = guide.SubTitle.Trim();
        var (description, contents) = SiteTravelGuides.Contents(guide.Description, subTitle);
        return new SiteTravelGuide
        {
            Id = guide.Id,
            Title = guide.Title,
            SubTitle = subTitle,
            Slug = SiteTravelGuides.Slug(guide.UrlFriendlyTitle, guide.Title),
            Description = description,
            ImagePath = guide.ImagePath,
            Images = [.. new[] { guide.ImagePath }.Concat(guide.Gallery).Where(i => !string.IsNullOrEmpty(i)).Select(i => i!).Distinct()],
            Published = guide.CreatedOn,
            Author = $"{author?.FirstName} {author?.LastName}".Trim() is { Length: > 0 } name ? name : null,
            MetaKeywords = guide.MetaKeywords,
            MetaDescription = guide.MetaDescription,
            Contents = contents,
            Related = [.. related.OrderBy(r => others.IndexOf(r.Id))],
        };
    }
}

internal static partial class SiteTravelGuides
{
    /// <summary>Legacy: the guide's own URL title, else one made from its title.</summary>
    public static string Slug(string? urlFriendlyTitle, string title) => SiteCache.Slug(urlFriendlyTitle, title);

    public static async Task<IReadOnlyList<SiteTravelGuideCard>> Cards(IQueryable<Domain.Catalog.TravelGuides.TravelGuide> guides, CancellationToken cancellationToken) =>
        [.. (await guides.Select(g => new { g.Id, g.Title, g.UrlFriendlyTitle, g.ShortDescription, g.ImagePath, g.CreatedOn }).ToListAsync(cancellationToken))
            .Select(g => new SiteTravelGuideCard
            {
                Id = g.Id, Title = g.Title, Slug = Slug(g.UrlFriendlyTitle, g.Title), ShortDescription = g.ShortDescription, ImagePath = g.ImagePath, Published = g.CreatedOn,
            })];

    /// <summary>
    /// Legacy's table of contents: the subtitle (shown above the text), then each h2–h5 in the text, each given an
    /// id made from its words ("Best Beaches" → "bestBeaches", as legacy's anchors were; a repeat gets "-2").
    /// </summary>
    public static (string Html, IReadOnlyList<SiteTravelGuideHeading> Headings) Contents(string? html, string subTitle)
    {
        var headings = new List<SiteTravelGuideHeading>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "introduction", "travelGuides" }; // the page's own
        string Unique(string id)
        {
            var unique = id;
            for (var n = 2; !used.Add(unique); n++)
                unique = $"{id}-{n}";
            return unique;
        }

        if (subTitle.Length > 0)
            headings.Add(new SiteTravelGuideHeading(Unique(CamelCase(subTitle) is { Length: > 0 } sub ? sub : "overview"), subTitle));
        var result = Heading().Replace(html ?? "", match =>
        {
            var title = WebUtility.HtmlDecode(Tags().Replace(match.Groups["inner"].Value, "")).Trim();
            var id = CamelCase(title);
            if (id.Length == 0)
                return match.Value;
            var unique = Unique(id);
            headings.Add(new SiteTravelGuideHeading(unique, title));
            var attributes = IdAttribute().Replace(match.Groups["attributes"].Value, "");
            return $"<{match.Groups["tag"].Value}{attributes} id=\"{unique}\">{match.Groups["inner"].Value}</{match.Groups["tag"].Value}>";
        });
        return (result, headings);
    }

    /// <summary>Legacy ConvertToCamelCase: letters and digits only, first word lower case, the rest capitalised.</summary>
    private static string CamelCase(string text)
    {
        var words = NotWordCharacters().Replace(text, "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var info = CultureInfo.InvariantCulture.TextInfo;
        return string.Concat(words.Select((w, i) => i == 0 ? w.ToLowerInvariant() : info.ToTitleCase(w.ToLowerInvariant())));
    }

    [GeneratedRegex(@"<(?<tag>h[2-5])(?<attributes>\s[^>]*)?>(?<inner>.*?)</\k<tag>>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"\s+id\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex IdAttribute();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[^a-zA-Z0-9\s]")]
    private static partial Regex NotWordCharacters();
}
