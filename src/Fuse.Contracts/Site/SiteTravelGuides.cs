namespace Fuse.Contracts.Site;

// The website's Blogs & Tips (legacy TravelGuides and TravelGuide pages): /blogs-and-tips, newest first, seven to a
// page, and each guide at /blogs-and-tips/{slug}.

public sealed record SiteTravelGuideCard
{
    public DefaultIdType Id { get; init; }
    public string Title { get; init; } = "";
    public string Slug { get; init; } = "";
    /// <summary>Rich text (HTML).</summary>
    public string? ShortDescription { get; init; }
    public string? ImagePath { get; init; }
    public DateTime Published { get; init; }
}

/// <summary>One page of guides (none past the last page); the first is shown large.</summary>
public sealed record SiteTravelGuidePage(IReadOnlyList<SiteTravelGuideCard> Guides, int Page, int TotalPages);

[Get("site/travel-guides"), Public]
public sealed record ListSiteTravelGuides(int Page = 1) : IQuery<SiteTravelGuidePage>;

/// <summary>A heading in a guide, for its table of contents (its id is set on the heading in <see cref="SiteTravelGuide.Description"/>).</summary>
public sealed record SiteTravelGuideHeading(string Id, string Title);

public sealed record SiteTravelGuide
{
    public DefaultIdType Id { get; init; }
    public string Title { get; init; } = "";
    public string SubTitle { get; init; } = "";
    public string Slug { get; init; } = "";
    /// <summary>Rich text (HTML), each heading given an id for <see cref="Contents"/>.</summary>
    public string Description { get; init; } = "";
    public string? ImagePath { get; init; }
    /// <summary>The main image then the gallery, for the header's slideshow.</summary>
    public IReadOnlyList<string> Images { get; init; } = [];
    public DateTime Published { get; init; }
    /// <summary>Who wrote it (the admin who created it).</summary>
    public string? Author { get; init; }
    public string? MetaKeywords { get; init; }
    public string? MetaDescription { get; init; }
    /// <summary>The subtitle first, then the guide's own headings.</summary>
    public IReadOnlyList<SiteTravelGuideHeading> Contents { get; init; } = [];
    /// <summary>Three other guides.</summary>
    public IReadOnlyList<SiteTravelGuideCard> Related { get; init; } = [];
}

[Get("site/travel-guides/{Slug}"), Public]
public sealed record GetSiteTravelGuide(string Slug) : IQuery<SiteTravelGuide>;
