using Microsoft.AspNetCore.Components;
using Fuse.Contracts.Api;
using Fuse.Contracts.Site;
using Fuse.Site.Components.Banners;
using Fuse.Site.Interop;

namespace Fuse.Site.Pages;

/// <summary>
/// A public page backed by a CMS record (<see cref="SitePages"/>): its SEO and banner headings load with any
/// other data the page needs (<see cref="LoadAsync"/>), once, while prerendering. Mark that data
/// <c>[PersistentState]</c> too, so the browser doesn't fetch it again.
/// </summary>
public abstract class SitePage : JsComponent
{
    [Inject] protected IApi Api { get; set; } = null!;

    [PersistentState] public PageSeo? Page { get; set; }

    /// <summary>The banner's headings (<paramref name="title"/> when the record has none); null while it loads.</summary>
    protected PageHeadings? BannerHeadings(string? title = null) => Page is null ? null : PageHeadings.Of(Page, title);

    /// <summary>The page's record in <see cref="SitePages"/>.</summary>
    protected abstract DefaultIdType PageId { get; }

    /// <summary>Loads the page's other data (runs alongside the record's load, only when not carried over).</summary>
    protected virtual Task LoadAsync() => Task.CompletedTask;

    protected override async Task OnInitializedAsync()
    {
        if (Page is not null)
            return;
        var page = Api.Send(new GetSitePage(PageId)).AsTask();
        await LoadAsync();
        Page = await page;
    }
}
