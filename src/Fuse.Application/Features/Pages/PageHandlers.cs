using Fuse.Contracts.Pages;
using Fuse.Domain.Catalog.Pages;
using Fuse.Domain.Catalog.SEO;

namespace Fuse.Application.Features.Pages;

internal sealed class SearchPagesHandler(IAppDbContext db) : IQueryHandler<SearchPages, PagedResult<PageSummary>>
{
    public ValueTask<PagedResult<PageSummary>> Handle(SearchPages query, CancellationToken cancellationToken)
    {
        var pages = db.Pages.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            pages = pages.Where(p => p.Title.Contains(query.Keyword) || (p.UrlSlug != null && p.UrlSlug.Contains(query.Keyword)));
        return new(pages.Select(p => new PageSummary { Id = p.Id, Title = p.Title, UrlSlug = p.UrlSlug })
            .OrderBy(query.OrderBy, p => p.Title)
            .ToPagedResultAsync(query, cancellationToken));
    }
}

internal sealed class ListPagesHandler(IAppDbContext db) : IQueryHandler<ListPages, IReadOnlyList<PageSummary>>
{
    public async ValueTask<IReadOnlyList<PageSummary>> Handle(ListPages query, CancellationToken cancellationToken) =>
        await db.Pages.AsNoTracking().OrderBy(p => p.Title)
            .Select(p => new PageSummary { Id = p.Id, Title = p.Title, UrlSlug = p.UrlSlug })
            .ToListAsync(cancellationToken);
}

internal sealed class GetPageEditorHandler(IAppDbContext db) : IQueryHandler<GetPageEditor, PageEditor>
{
    public async ValueTask<PageEditor> Handle(GetPageEditor query, CancellationToken cancellationToken)
    {
        var p = await db.Pages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken) ?? throw new NotFoundException("Page not found.");
        return new PageEditor(p.Id, p.Title, new PageInput
        {
            SeoPageTitle = p.SeoPageTitle,
            H1 = p.H1,
            H2 = p.H2,
            H3 = p.H3,
            UrlSlug = p.UrlSlug,
            MetaKeywords = p.MetaKeywords,
            MetaDescription = p.MetaDescription,
            CustomSeoScripts = p.CustomSeoScripts,
        });
    }
}

internal sealed class UpdatePageHandler(IAppDbContext db) : ICommandHandler<UpdatePage>
{
    public async ValueTask<Unit> Handle(UpdatePage command, CancellationToken cancellationToken)
    {
        var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Page not found.");
        if (PageRules.IsManagedElsewhere(page.Title))
            throw new BusinessRuleException("This page's SEO is edited with its tour or property.");

        var d = command.Details;
        page.ApplySeo(d.SeoPageTitle, d.H1, d.H2, PageRules.HasThirdHeading(page.Title) ? d.H3 : page.H3, d.MetaKeywords, d.MetaDescription, d.CustomSeoScripts);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class DeletePageHandler(IAppDbContext db) : ICommandHandler<DeletePage>
{
    public async ValueTask<Unit> Handle(DeletePage command, CancellationToken cancellationToken)
    {
        var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Page not found.");
        if (PageRules.IsManagedElsewhere(page.Title))
            throw new BusinessRuleException("This page belongs to a tour or property; it goes when they do.");
        db.PageModalLookups.RemoveRange(await db.PageModalLookups.Where(l => l.PageId == page.Id).ToListAsync(cancellationToken));
        db.Pages.Remove(page);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class SearchPageModalsHandler(IAppDbContext db) : IQueryHandler<SearchPageModals, PagedResult<PageModalSummary>>
{
    public ValueTask<PagedResult<PageModalSummary>> Handle(SearchPageModals query, CancellationToken cancellationToken)
    {
        var modals = db.PageModals.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            modals = modals.Where(m => m.Title != null && m.Title.Contains(query.Keyword));
        return new(modals
            .Select(m => new PageModalSummary
            {
                Id = m.Id,
                Title = m.Title,
                StartDate = m.StartDate,
                EndDate = m.EndDate,
                Pages = db.PageModalLookups.Count(l => l.PageModalId == m.Id && db.Pages.Any(p => p.Id == l.PageId)),
            })
            .OrderBy(query.OrderBy, m => m.Title)
            .ToPagedResultAsync(query, cancellationToken));
    }
}

internal sealed class GetPageModalHandler(IAppDbContext db) : IQueryHandler<GetPageModal, PageModalInput>
{
    public async ValueTask<PageModalInput> Handle(GetPageModal query, CancellationToken cancellationToken)
    {
        var m = await db.PageModals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken) ?? throw new NotFoundException("Page modal not found.");
        var pages = await db.PageModalLookups.Where(l => l.PageModalId == m.Id && db.Pages.Any(p => p.Id == l.PageId))
            .Select(l => l.PageId).ToListAsync(cancellationToken);
        return new PageModalInput
        {
            Title = m.Title ?? "",
            StartDate = m.StartDate?.Date,
            EndDate = m.EndDate?.Date,
            CallToAction = m.CallToAction,
            Description = m.Description,
            PageIds = pages,
        };
    }
}

internal sealed class SavePageModalHandler(IAppDbContext db) : ICommandHandler<SavePageModal, DefaultIdType>
{
    public async ValueTask<DefaultIdType> Handle(SavePageModal command, CancellationToken cancellationToken)
    {
        var d = command.Details;
        PageModal modal;
        List<PageModalLookup> links;
        if (command.Id is { } id)
        {
            modal = await db.PageModals.FirstOrDefaultAsync(m => m.Id == id, cancellationToken) ?? throw new NotFoundException("Page modal not found.");
            modal.Apply(d.Title, d.Description, d.CallToAction, d.StartDate, d.EndDate);
            links = await db.PageModalLookups.Where(l => l.PageModalId == id).ToListAsync(cancellationToken);
        }
        else
        {
            modal = PageModal.Create(d.Title, d.Description, d.CallToAction, d.StartDate, d.EndDate);
            db.PageModals.Add(modal);
            links = [];
        }

        var wanted = d.PageIds.Distinct().ToHashSet();
        if (await db.Pages.CountAsync(p => wanted.Contains(p.Id), cancellationToken) != wanted.Count)
            throw new RequestValidationException("Pages", "One of the chosen pages no longer exists.");
        db.PageModalLookups.RemoveRange(links.Where(l => !wanted.Contains(l.PageId)));
        db.PageModalLookups.AddRange(wanted.Where(p => links.All(l => l.PageId != p)).Select(p => new PageModalLookup(DefaultIdType.NewGuid(), p, modal.Id)));

        await db.SaveChangesAsync(cancellationToken);
        return modal.Id;
    }
}

internal sealed class DeletePageModalHandler(IAppDbContext db) : ICommandHandler<DeletePageModal>
{
    public async ValueTask<Unit> Handle(DeletePageModal command, CancellationToken cancellationToken)
    {
        var modal = await db.PageModals.FirstOrDefaultAsync(m => m.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Page modal not found.");
        db.PageModalLookups.RemoveRange(await db.PageModalLookups.Where(l => l.PageModalId == modal.Id).ToListAsync(cancellationToken));
        db.PageModals.Remove(modal);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class GetGlobalSeoHandler(IAppDbContext db) : IQueryHandler<GetGlobalSeo, GlobalSeo>
{
    public async ValueTask<GlobalSeo> Handle(GetGlobalSeo query, CancellationToken cancellationToken)
    {
        var seo = await db.Seo.AsNoTracking().OrderBy(s => s.CreatedOn).FirstOrDefaultAsync(cancellationToken);
        return new GlobalSeo { Breadcrumbs = seo?.Breadcrumbs, SearchAction = seo?.SearchAction };
    }
}

/// <summary>There's one global SEO row per tenant; the first save creates it.</summary>
internal sealed class SaveGlobalSeoHandler(IAppDbContext db) : ICommandHandler<SaveGlobalSeo>
{
    public async ValueTask<Unit> Handle(SaveGlobalSeo command, CancellationToken cancellationToken)
    {
        var seo = await db.Seo.OrderBy(s => s.CreatedOn).FirstOrDefaultAsync(cancellationToken);
        if (seo is null)
        {
            seo = new Seo();
            db.Seo.Add(seo);
        }
        seo.Set(command.Seo.Breadcrumbs, command.Seo.SearchAction);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class SearchPageRedirectsHandler(IAppDbContext db) : IQueryHandler<SearchPageRedirects, PagedResult<PageRedirect>>
{
    public ValueTask<PagedResult<PageRedirect>> Handle(SearchPageRedirects query, CancellationToken cancellationToken)
    {
        var redirects = db.SeoRedirects.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            redirects = redirects.Where(r => r.Url.Contains(query.Keyword) || r.RedirectUrl.Contains(query.Keyword));
        return new(redirects.Select(r => new PageRedirect { Id = r.Id, Url = r.Url, RedirectUrl = r.RedirectUrl })
            .OrderBy(query.OrderBy, r => r.Url)
            .ToPagedResultAsync(query, cancellationToken));
    }
}

internal sealed class SavePageRedirectUniqueValidator : AbstractValidator<SavePageRedirect>
{
    public SavePageRedirectUniqueValidator(IAppDbContext db) =>
        RuleFor(x => x.Url)
            .MustAsync(async (command, url, ct) => !await db.SeoRedirects.AnyAsync(r => r.Url == PageRedirectUrls.Normalise(url) && r.Id != command.Id, ct))
            .WithName("URL").WithMessage("That URL already redirects somewhere.");
}

internal static class PageRedirectUrls
{
    /// <summary>Stored as entered, trimmed (existing redirects are paths like "/travel-guides/x").</summary>
    public static string Normalise(string url) => url.Trim();
}

internal sealed class SavePageRedirectHandler(IAppDbContext db) : ICommandHandler<SavePageRedirect, DefaultIdType>
{
    public async ValueTask<DefaultIdType> Handle(SavePageRedirect command, CancellationToken cancellationToken)
    {
        var (url, target) = (PageRedirectUrls.Normalise(command.Url), PageRedirectUrls.Normalise(command.RedirectUrl));
        SeoRedirect redirect;
        if (command.Id is { } id)
        {
            redirect = await db.SeoRedirects.FirstOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw new NotFoundException("Redirect not found.");
            (redirect.Url, redirect.RedirectUrl) = (url, target);
        }
        else
        {
            redirect = new SeoRedirect(url, target);
            db.SeoRedirects.Add(redirect);
        }
        await db.SaveChangesAsync(cancellationToken);
        return redirect.Id;
    }
}

internal sealed class DeletePageRedirectHandler(IAppDbContext db) : ICommandHandler<DeletePageRedirect>
{
    public async ValueTask<Unit> Handle(DeletePageRedirect command, CancellationToken cancellationToken)
    {
        var redirect = await db.SeoRedirects.FirstOrDefaultAsync(r => r.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Redirect not found.");
        db.SeoRedirects.Remove(redirect);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
