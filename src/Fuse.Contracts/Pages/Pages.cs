namespace Fuse.Contracts.Pages;

// Pages -----------------------------------------------------------------------------------

public sealed record PageSummary
{
    public DefaultIdType Id { get; init; }
    public string Title { get; init; } = "";
    public string? UrlSlug { get; init; }

    /// <summary>Tour and property pages are kept in step by the Tours and Properties screens.</summary>
    [NotSortable]
    public bool ManagedElsewhere => PageRules.IsManagedElsewhere(Title);
}

public static class PageRules
{
    public static bool IsManagedElsewhere(string title) =>
        title.StartsWith("Tours - ", StringComparison.Ordinal) || title.StartsWith("Hostels - ", StringComparison.Ordinal);

    /// <summary>Only the home page has a third heading (legacy).</summary>
    public static bool HasThirdHeading(string title) => title == "Home";
}

[Post("pages/search"), Permission(FuseAction.Search, FuseResource.Pages)]
public sealed record SearchPages : PagedQuery, IQuery<PagedResult<PageSummary>>;

[Get("pages/all"), Permission(FuseAction.View, FuseResource.Pages)]
public sealed record ListPages : IQuery<IReadOnlyList<PageSummary>>;

public sealed record PageInput : ISeoFields
{
    public string? SeoPageTitle { get; set; }
    public string? H1 { get; set; }
    public string? H2 { get; set; }
    public string? H3 { get; set; }
    public string? UrlSlug { get; set; }
    public string? MetaKeywords { get; set; }
    public string? MetaDescription { get; set; }
    public string? CustomSeoScripts { get; set; }
}

public sealed record PageEditor(DefaultIdType Id, string Title, PageInput Details);

[Get("pages/{Id}"), Permission(FuseAction.View, FuseResource.Pages)]
public sealed record GetPageEditor(DefaultIdType Id) : IQuery<PageEditor>;

/// <summary>Updates a page's SEO. Pages are created by the website (and by tours / properties), never here.</summary>
[Put("pages/{Id}"), Permission(FuseAction.Update, FuseResource.Pages)]
public sealed record UpdatePage(DefaultIdType Id, PageInput Details) : ICommand;

[Delete("pages/{Id}"), Permission(FuseAction.Delete, FuseResource.Pages)]
public sealed record DeletePage(DefaultIdType Id) : ICommand;

// Page modals -----------------------------------------------------------------------------

public sealed record PageModalSummary
{
    public DefaultIdType Id { get; init; }
    public string? Title { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public int Pages { get; init; }
}

[Post("page-modals/search"), Permission(FuseAction.Search, FuseResource.Pages)]
public sealed record SearchPageModals : PagedQuery, IQuery<PagedResult<PageModalSummary>>;

public sealed record PageModalInput
{
    public string Title { get; set; } = "";
    /// <summary>Shown from the start of <see cref="StartDate"/> to the end of <see cref="EndDate"/>; both empty = always.</summary>
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? CallToAction { get; set; }
    public string? Description { get; set; }
    public List<DefaultIdType> PageIds { get; set; } = [];
}

[Get("page-modals/{Id}"), Permission(FuseAction.View, FuseResource.Pages)]
public sealed record GetPageModal(DefaultIdType Id) : IQuery<PageModalInput>;

[Put("page-modals"), Permission(FuseAction.Update, FuseResource.Pages)]
public sealed record SavePageModal(DefaultIdType? Id, PageModalInput Details) : ICommand<DefaultIdType>;

[Delete("page-modals/{Id}"), Permission(FuseAction.Delete, FuseResource.Pages)]
public sealed record DeletePageModal(DefaultIdType Id) : ICommand;

public sealed class SavePageModalValidator : AbstractValidator<SavePageModal>
{
    public SavePageModalValidator()
    {
        RuleFor(x => x.Details.Title).NotEmpty().MaximumLength(1024).WithName("Title");
        RuleFor(x => x.Details.PageIds).NotEmpty().WithName("Pages").WithMessage("Choose the pages the modal shows on.");
        RuleFor(x => x.Details.EndDate).NotNull().When(x => x.Details.StartDate.HasValue).WithName("Dates").WithMessage("Choose both dates, or neither.");
        RuleFor(x => x.Details.StartDate).NotNull().When(x => x.Details.EndDate.HasValue).WithName("Dates").WithMessage("Choose both dates, or neither.");
        RuleFor(x => x.Details.EndDate).GreaterThanOrEqualTo(x => x.Details.StartDate).When(x => x.Details.StartDate.HasValue && x.Details.EndDate.HasValue)
            .WithName("Dates").WithMessage("The modal can't end before it starts.");
    }
}

// Global SEO ------------------------------------------------------------------------------

/// <summary>Site-wide structured data: the breadcrumbs and search-action scripts in every page head.</summary>
public sealed record GlobalSeo
{
    public string? Breadcrumbs { get; set; }
    public string? SearchAction { get; set; }
}

[Get("seo"), Permission(FuseAction.View, FuseResource.Seo)]
public sealed record GetGlobalSeo : IQuery<GlobalSeo>;

[Put("seo"), Permission(FuseAction.Update, FuseResource.Seo)]
public sealed record SaveGlobalSeo(GlobalSeo Seo) : ICommand;

// Page redirects --------------------------------------------------------------------------

public sealed record PageRedirect
{
    public DefaultIdType Id { get; init; }
    public string Url { get; set; } = "";
    public string RedirectUrl { get; set; } = "";
}

[Post("page-redirects/search"), Permission(FuseAction.Search, FuseResource.Seo)]
public sealed record SearchPageRedirects : PagedQuery, IQuery<PagedResult<PageRedirect>>;

/// <summary>Creates (no <see cref="Id"/>) or updates a redirect from one page slug to another.</summary>
[Put("page-redirects"), Permission(FuseAction.Update, FuseResource.Seo)]
public sealed record SavePageRedirect(DefaultIdType? Id, string Url, string RedirectUrl) : ICommand<DefaultIdType>;

[Delete("page-redirects/{Id}"), Permission(FuseAction.Delete, FuseResource.Seo)]
public sealed record DeletePageRedirect(DefaultIdType Id) : ICommand;

public sealed class SavePageRedirectValidator : AbstractValidator<SavePageRedirect>
{
    /// <summary>A path on this site ("/travel-guides/this-page" or "this-page"), not a full web address.</summary>
    private const string Slug = "^[A-Za-z0-9\\-_./~%]+$";

    private static bool IsSitePath(string? url) =>
        url is not null && !url.Contains("://") && System.Text.RegularExpressions.Regex.IsMatch(url.Trim(), Slug);

    public SavePageRedirectValidator()
    {
        RuleFor(x => x.Url).NotEmpty().MaximumLength(1024).Must(IsSitePath).WithName("URL")
            .WithMessage("URL must be an address on this site, not a full web address, e.g. /travel-guides/old-guide.");
        RuleFor(x => x.RedirectUrl).NotEmpty().MaximumLength(1024).Must(IsSitePath).WithName("Redirect URL")
            .WithMessage("Redirect URL must be an address on this site, not a full web address, e.g. /blogs-and-tips/new-guide.");
        RuleFor(x => x.RedirectUrl).Must((x, target) => !string.Equals(target?.Trim(), x.Url?.Trim(), StringComparison.OrdinalIgnoreCase)).WithName("Redirect URL")
            .WithMessage("A page can't redirect to itself.");
    }
}
