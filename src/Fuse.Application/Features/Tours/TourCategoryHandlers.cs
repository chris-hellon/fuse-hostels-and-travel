using Fuse.Application.Common.Files;
using Fuse.Contracts.Tours;
using Fuse.Domain.Catalog.Tours;

namespace Fuse.Application.Features.Tours;

// Fuse uses one level of categories (Tour.TourCategoryId). The legacy "groups" and category
// lookups only served other tenants and are not carried forward.

internal static class TourCategoryProjection
{
    public static IQueryable<TourCategoryItem> ToItems(this IQueryable<TourCategory> categories, IAppDbContext db) =>
        categories.Select(c => new TourCategoryItem
        {
            Id = c.Id,
            Name = c.Name,
            ThumbnailImagePath = c.ThumbnailImagePath,
            Tours = db.Tours.Count(t => t.TourCategoryId == c.Id),
        });
}

internal sealed class ListTourCategoriesHandler(IAppDbContext db) : IQueryHandler<ListTourCategories, IReadOnlyList<TourCategoryItem>>
{
    public async ValueTask<IReadOnlyList<TourCategoryItem>> Handle(ListTourCategories query, CancellationToken cancellationToken) =>
        await db.TourCategories.AsNoTracking().ToItems(db).OrderBy(c => c.Name).ToListAsync(cancellationToken);
}

internal sealed class SearchTourCategoriesHandler(IAppDbContext db) : IQueryHandler<SearchTourCategories, PagedResult<TourCategoryItem>>
{
    public ValueTask<PagedResult<TourCategoryItem>> Handle(SearchTourCategories query, CancellationToken cancellationToken)
    {
        var categories = db.TourCategories.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            categories = categories.Where(c => c.Name.Contains(query.Keyword));

        return new(categories.ToItems(db)
            .OrderBy(query.OrderBy, c => c.Name)
            .ToPagedResultAsync(query, cancellationToken));
    }
}

internal sealed class GetTourCategoryHandler(IAppDbContext db) : IQueryHandler<GetTourCategory, TourCategoryDetails>
{
    public async ValueTask<TourCategoryDetails> Handle(GetTourCategory query, CancellationToken cancellationToken)
    {
        var category = await db.TourCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == query.Id, cancellationToken)
                       ?? throw new NotFoundException("Category not found.");
        return new TourCategoryDetails(category.Id,
            new TourCategoryInput { Name = category.Name, ShortDescription = category.ShortDescription, Description = category.Description },
            string.IsNullOrEmpty(category.ImagePath) ? null : category.ImagePath);
    }
}

internal sealed class SaveTourCategoryNameValidator : AbstractValidator<SaveTourCategory>
{
    public SaveTourCategoryNameValidator(IAppDbContext db) =>
        RuleFor(x => x.Details.Name)
            .MustAsync(async (command, name, ct) => !await db.TourCategories.AnyAsync(c => c.Name == name.Trim() && c.Id != command.Id, ct))
            .WithName("Name").WithMessage("A category with this name already exists.");
}

internal sealed class SaveTourCategoryHandler(IAppDbContext db, IFileStore files) : ICommandHandler<SaveTourCategory, DefaultIdType>
{
    public async ValueTask<DefaultIdType> Handle(SaveTourCategory command, CancellationToken cancellationToken)
    {
        var input = command.Details;
        TourCategory category;
        if (command.Id is { } id)
        {
            category = await db.TourCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw new NotFoundException("Category not found.");
            category.Rename(input.Name, input.ShortDescription, input.Description);
        }
        else
        {
            category = TourCategory.CreateCategory(input.Name, input.ShortDescription, input.Description);
            db.TourCategories.Add(category);
        }

        // The old file stays in storage, as legacy (it may be shared with the site).
        if (command.Image is not null || command.RemoveImage)
        {
            category.SetImage(command.Image is null ? null : await files.SaveAsync(command.Image, FileKind.Image, cancellationToken));
        }

        await db.SaveChangesAsync(cancellationToken);
        return category.Id;
    }
}

internal sealed class DeleteTourCategoryHandler(IAppDbContext db) : ICommandHandler<DeleteTourCategory>
{
    public async ValueTask<Unit> Handle(DeleteTourCategory command, CancellationToken cancellationToken)
    {
        var category = await db.TourCategories.FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken)
                       ?? throw new NotFoundException("Category not found.");
        if (await db.Tours.AnyAsync(t => t.TourCategoryId == category.Id, cancellationToken))
            throw new BusinessRuleException($"Tours are still in {category.Name}. Move them to another category first.");

        db.TourCategories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
