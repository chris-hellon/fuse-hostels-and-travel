using Fuse.Application.Common.Files;
using Fuse.Contracts.Galleries;
using Fuse.Domain.Catalog.Galleries;

namespace Fuse.Application.Features.Galleries;

internal sealed class SearchGalleriesHandler(IAppDbContext db) : IQueryHandler<SearchGalleries, PagedResult<GallerySummary>>
{
    public ValueTask<PagedResult<GallerySummary>> Handle(SearchGalleries query, CancellationToken cancellationToken)
    {
        var galleries = db.Galleries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            galleries = galleries.Where(g => g.Title.Contains(query.Keyword));

        return new(galleries
            .Select(g => new GallerySummary
            {
                Id = g.Id,
                Title = g.Title,
                Images = db.GalleryImages.Count(i => i.GalleryId == g.Id),
                CreatedOn = g.CreatedOn,
            })
            .OrderBy(query.OrderBy, g => g.CreatedOn, descending: true)
            .ToPagedResultAsync(query, cancellationToken));
    }
}

internal sealed class GetGalleryEditorHandler(IAppDbContext db) : IQueryHandler<GetGalleryEditor, GalleryEditor>
{
    public async ValueTask<GalleryEditor> Handle(GetGalleryEditor query, CancellationToken cancellationToken)
    {
        var g = await db.Galleries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken) ?? throw new NotFoundException("Gallery not found.");
        var images = await db.GalleryImages.AsNoTracking()
            .Where(i => i.GalleryId == g.Id && i.ImagePath != null && i.ImagePath != "")
            .OrderBy(i => i.SortOrder).ThenBy(i => i.CreatedOn)
            .Select(i => new GalleryImageItem(i.Id, i.ImagePath!, i.ImagePath!))
            .ToListAsync(cancellationToken);
        return new GalleryEditor(g.Id, new GalleryInput
        {
            Title = g.Title,
            Description = g.Description,
            MetaKeywords = g.MetaKeywords,
            MetaDescription = g.MetaDescription,
        }, images);
    }
}

internal sealed class SaveGalleryHandler(IAppDbContext db, IFileStore files) : ICommandHandler<SaveGallery, DefaultIdType>
{
    public async ValueTask<DefaultIdType> Handle(SaveGallery command, CancellationToken cancellationToken)
    {
        var d = command.Details;
        Gallery gallery;
        List<GalleryImage> existing;
        if (command.Id is { } id)
        {
            gallery = await db.Galleries.FirstOrDefaultAsync(g => g.Id == id, cancellationToken) ?? throw new NotFoundException("Gallery not found.");
            gallery.Apply(d.Title, d.Description, d.MetaKeywords, d.MetaDescription);
            existing = await db.GalleryImages.Where(i => i.GalleryId == id).ToListAsync(cancellationToken);
        }
        else
        {
            gallery = Gallery.Create(d.Title, d.Description, d.MetaKeywords, d.MetaDescription);
            db.Galleries.Add(gallery);
            existing = [];
        }

        // Images in the given order: kept ones move, new ones must be files this store issued.
        var kept = new HashSet<DefaultIdType>();
        for (var index = 0; index < command.Images.Count; index++)
        {
            var item = command.Images[index];
            if (item.Id is { } imageId)
            {
                var image = existing.FirstOrDefault(i => i.Id == imageId)
                            ?? throw new BusinessRuleException("An image was removed by someone else. Reopen the gallery and try again.");
                image.MoveTo(index);
                kept.Add(imageId);
            }
            else if (item.Url is { } url && files.IsTenantFile(url, FileKind.Image))
            {
                db.GalleryImages.Add(GalleryImage.Create(gallery.Id, gallery.Title, url, index));
            }
            else
            {
                throw new RequestValidationException("Images", "An image wasn't uploaded here. Add it again.");
            }
        }

        // Removed images are soft-deleted rows; their files stay in storage, as legacy.
        db.GalleryImages.RemoveRange(existing.Where(i => !kept.Contains(i.Id)));
        await db.SaveChangesAsync(cancellationToken);
        return gallery.Id;
    }
}

internal sealed class DeleteGalleryHandler(IAppDbContext db) : ICommandHandler<DeleteGallery>
{
    public async ValueTask<Unit> Handle(DeleteGallery command, CancellationToken cancellationToken)
    {
        var gallery = await db.Galleries.FirstOrDefaultAsync(g => g.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Gallery not found.");
        db.Galleries.Remove(gallery);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class UploadGalleryImageHandler(IFileStore files) : ICommandHandler<UploadGalleryImage, string>
{
    public async ValueTask<string> Handle(UploadGalleryImage command, CancellationToken cancellationToken) =>
        await files.SaveAsync(command.File, FileKind.Image, cancellationToken);
}
