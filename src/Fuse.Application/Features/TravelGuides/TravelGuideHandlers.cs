using Fuse.Application.Common.Files;
using Fuse.Contracts.TravelGuides;
using Fuse.Domain.Catalog.TravelGuides;
using Fuse.Shared.Text;

namespace Fuse.Application.Features.TravelGuides;

internal sealed class SearchTravelGuidesHandler(IAppDbContext db) : IQueryHandler<SearchTravelGuides, PagedResult<TravelGuideSummary>>
{
    public ValueTask<PagedResult<TravelGuideSummary>> Handle(SearchTravelGuides query, CancellationToken cancellationToken)
    {
        var guides = db.TravelGuides.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            guides = guides.Where(g => g.Title.Contains(query.Keyword) || (g.UrlFriendlyTitle != null && g.UrlFriendlyTitle.Contains(query.Keyword)));

        // Newest first by default, as legacy.
        return new(guides
            .Select(g => new TravelGuideSummary
            {
                Id = g.Id,
                Title = g.Title,
                UrlSlug = g.UrlFriendlyTitle,
                CreatedOn = g.CreatedOn,
                LastModifiedOn = g.LastModifiedOn,
            })
            .OrderBy(query.OrderBy, g => g.CreatedOn, descending: true)
            .ToPagedResultAsync(query, cancellationToken));
    }
}

internal sealed class GetTravelGuideEditorHandler(IAppDbContext db) : IQueryHandler<GetTravelGuideEditor, TravelGuideEditor>
{
    public async ValueTask<TravelGuideEditor> Handle(GetTravelGuideEditor query, CancellationToken cancellationToken)
    {
        var g = await db.TravelGuides.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
                ?? throw new NotFoundException("Travel guide not found.");
        var gallery = await db.TravelGuideGalleryImages.AsNoTracking()
            .Where(i => i.TravelGuideId == g.Id && i.ImagePath != null && i.ImagePath != "")
            .OrderBy(i => i.SortOrder).ThenBy(i => i.CreatedOn)
            .Select(i => new GalleryImageItem(i.Id, i.ImagePath!, i.ImagePath!))
            .ToListAsync(cancellationToken);

        return new TravelGuideEditor
        {
            Id = g.Id,
            ImagePath = string.IsNullOrEmpty(g.ImagePath) ? null : g.ImagePath,
            Gallery = gallery,
            Details = new TravelGuideInput
            {
                Title = g.Title,
                SubTitle = g.SubTitle,
                ShortDescription = g.ShortDescription,
                Description = g.Description,
                UrlSlug = g.UrlFriendlyTitle,
                MetaKeywords = g.MetaKeywords,
                MetaDescription = g.MetaDescription,
            },
        };
    }
}

/// <summary>Titles and slugs are unique: the website finds a guide by its slug.</summary>
internal static class TravelGuideUniqueness
{
    public static void AddRules<T>(AbstractValidator<T> validator, IAppDbContext db, Func<T, TravelGuideChanges> changes, Func<T, DefaultIdType?> id)
    {
        validator.RuleFor(x => changes(x).Details.Title)
            .MustAsync(async (command, title, ct) => !await db.TravelGuides.AnyAsync(g => g.Title == title.Trim() && g.Id != id(command), ct))
            .WithName("Title").WithMessage("A travel guide with this title already exists.");
        validator.RuleFor(x => changes(x).Details)
            .MustAsync(async (command, details, ct) =>
            {
                var slug = TravelGuideMapping.SlugFor(details);
                return slug.Length > 0 && !await db.TravelGuides.AnyAsync(g => g.UrlFriendlyTitle == slug && g.Id != id(command), ct);
            })
            .WithName("URL slug").WithMessage("Another travel guide already uses this URL slug.");
    }
}

internal sealed class CreateTravelGuideUniquenessValidator : AbstractValidator<CreateTravelGuide>
{
    public CreateTravelGuideUniquenessValidator(IAppDbContext db) => TravelGuideUniqueness.AddRules(this, db, x => x.Changes, _ => null);
}

internal sealed class UpdateTravelGuideUniquenessValidator : AbstractValidator<UpdateTravelGuide>
{
    public UpdateTravelGuideUniquenessValidator(IAppDbContext db) => TravelGuideUniqueness.AddRules(this, db, x => x.Changes, x => x.Id);
}

internal sealed class CreateTravelGuideHandler(IAppDbContext db, IFileStore files) : ICommandHandler<CreateTravelGuide, DefaultIdType>
{
    public async ValueTask<DefaultIdType> Handle(CreateTravelGuide command, CancellationToken cancellationToken)
    {
        var guide = TravelGuide.Create(command.Changes.Details.ToDomain());
        db.TravelGuides.Add(guide);
        await new TravelGuideChangeSet(db, files, guide, isNew: true).ApplyAsync(command.Changes, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return guide.Id;
    }
}

internal sealed class UpdateTravelGuideHandler(IAppDbContext db, IFileStore files) : ICommandHandler<UpdateTravelGuide>
{
    public async ValueTask<Unit> Handle(UpdateTravelGuide command, CancellationToken cancellationToken)
    {
        var guide = await db.TravelGuides.FirstOrDefaultAsync(g => g.Id == command.Id, cancellationToken)
                    ?? throw new NotFoundException("Travel guide not found.");
        guide.Apply(command.Changes.Details.ToDomain());
        await new TravelGuideChangeSet(db, files, guide, isNew: false).ApplyAsync(command.Changes, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class DeleteTravelGuideHandler(IAppDbContext db) : ICommandHandler<DeleteTravelGuide>
{
    public async ValueTask<Unit> Handle(DeleteTravelGuide command, CancellationToken cancellationToken)
    {
        var guide = await db.TravelGuides.FirstOrDefaultAsync(g => g.Id == command.Id, cancellationToken)
                    ?? throw new NotFoundException("Travel guide not found.");
        db.TravelGuides.Remove(guide);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class UploadTravelGuideImageHandler(IFileStore files) : ICommandHandler<UploadTravelGuideImage, string>
{
    public async ValueTask<string> Handle(UploadTravelGuideImage command, CancellationToken cancellationToken) =>
        await files.SaveAsync(command.File, FileKind.Image, cancellationToken);
}

/// <summary>Applies the banner and slideshow of a save. Replaced or removed files stay in storage, as legacy.</summary>
internal sealed class TravelGuideChangeSet(IAppDbContext db, IFileStore files, TravelGuide guide, bool isNew)
{
    public async Task ApplyAsync(TravelGuideChanges changes, CancellationToken cancellationToken)
    {
        if (changes.Banner != guide.ImagePath)
        {
            if (changes.Banner is { } banner && !files.IsTenantFile(banner, FileKind.Image))
                throw new RequestValidationException("Banner", "The banner image wasn't uploaded here. Choose it again.");
            guide.SetImage(changes.Banner);
        }

        var existing = isNew ? [] : await db.TravelGuideGalleryImages.Where(i => i.TravelGuideId == guide.Id).ToListAsync(cancellationToken);
        var kept = new HashSet<DefaultIdType>();
        for (var index = 0; index < changes.Gallery.Count; index++)
        {
            var item = changes.Gallery[index];
            if (item.Id is { } id)
            {
                var image = existing.FirstOrDefault(i => i.Id == id)
                            ?? throw new BusinessRuleException("A slideshow image was removed by someone else. Reopen the guide and try again.");
                image.MoveTo(index);
                kept.Add(id);
            }
            else if (item.Url is { } url && files.IsTenantFile(url, FileKind.Image))
            {
                db.TravelGuideGalleryImages.Add(TravelGuideGalleryImage.Create(guide.Id, guide.Title, url, index));
            }
            else
            {
                throw new RequestValidationException("Gallery", "A slideshow image wasn't uploaded here. Add it again.");
            }
        }

        db.TravelGuideGalleryImages.RemoveRange(existing.Where(i => !kept.Contains(i.Id)));
    }
}

internal static class TravelGuideMapping
{
    public static string SlugFor(TravelGuideInput input) =>
        string.IsNullOrWhiteSpace(input.UrlSlug) ? Slugs.From(input.Title) : input.UrlSlug.Trim();

    public static TravelGuideDetails ToDomain(this TravelGuideInput input) => new()
    {
        Title = input.Title,
        SubTitle = input.SubTitle,
        ShortDescription = input.ShortDescription,
        Description = input.Description,
        UrlSlug = SlugFor(input),
        MetaKeywords = input.MetaKeywords,
        MetaDescription = input.MetaDescription,
    };
}
