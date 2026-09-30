using Fuse.Contracts.Site;

namespace Fuse.Site.Components.Banners;

/// <summary>A banner's title and the smaller lines around it.</summary>
public sealed record PageHeadings(string Title, string? SubTitle, string? SubSubTitle)
{
    /// <summary>
    /// Legacy GetTitleSubTitleAndSubSubTitle: the page record's H2 (else H1, else H3) is the title, the
    /// other headings the smaller lines, all upper-cased; the defaults fill any gaps. With no default title
    /// the page's own name is used.
    /// </summary>
    public static PageHeadings Of(PageSeo page, string? title = null, string? subTitle = null, string? subSubTitle = null)
    {
        var h1 = Upper(page.H1);
        var h2 = Upper(page.H2);
        var h3 = Upper(page.H3);
        if (h2 is not null)
            return new(h2, h1 ?? subTitle, h3 ?? subSubTitle);
        if (h1 is not null)
            return new(h1, h3 ?? subTitle, subSubTitle);
        if (h3 is not null)
            return new(h3, subTitle, subSubTitle);
        return new((string.IsNullOrEmpty(title) ? page.Name ?? "" : title).ToUpperInvariant(), subTitle, subSubTitle);
    }

    /// <summary>
    /// Legacy's rule for a hostel's (or tour's) own page: H1 (else H2, else the name) is the title, H2 the line
    /// above it (unless it would repeat the title). Not upper-cased: the banner's style does that.
    /// </summary>
    public static PageHeadings ForRecord(PageSeo page, string name)
    {
        var title = !string.IsNullOrEmpty(page.H1) ? page.H1 : !string.IsNullOrEmpty(page.H2) ? page.H2 : name;
        var subTitle = !string.IsNullOrEmpty(page.H1) || page.H2 != title ? page.H2 : null;
        return new(title, subTitle, null);
    }

    private static string? Upper(string? value) => string.IsNullOrEmpty(value) ? null : value.ToUpperInvariant();
}
