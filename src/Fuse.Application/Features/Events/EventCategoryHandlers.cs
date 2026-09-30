using Fuse.Contracts.Events;
using Fuse.Domain.Catalog.Events;

namespace Fuse.Application.Features.Events;

internal static class EventCategoryProjection
{
    public static IQueryable<EventCategoryItem> ToItems(this IQueryable<EventCategory> categories, IAppDbContext db) =>
        categories.Select(c => new EventCategoryItem
        {
            Id = c.Id,
            Name = c.Name,
            Colour = c.Color,
            SortOrder = c.SortOrder,
            Events = db.Events.Count(e => e.EventCategoryId == c.Id),
        });
}

internal sealed class ListEventCategoriesHandler(IAppDbContext db) : IQueryHandler<ListEventCategories, IReadOnlyList<EventCategoryItem>>
{
    public async ValueTask<IReadOnlyList<EventCategoryItem>> Handle(ListEventCategories query, CancellationToken cancellationToken) =>
        await db.EventCategories.AsNoTracking().ToItems(db).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);
}

internal sealed class SearchEventCategoriesHandler(IAppDbContext db) : IQueryHandler<SearchEventCategories, PagedResult<EventCategoryItem>>
{
    public ValueTask<PagedResult<EventCategoryItem>> Handle(SearchEventCategories query, CancellationToken cancellationToken)
    {
        var categories = db.EventCategories.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            categories = categories.Where(c => c.Name.Contains(query.Keyword));

        return new(categories.ToItems(db)
            .OrderBy(query.OrderBy, c => c.SortOrder)
            .ToPagedResultAsync(query, cancellationToken));
    }
}

internal sealed class SaveEventCategoryNameValidator : AbstractValidator<SaveEventCategory>
{
    public SaveEventCategoryNameValidator(IAppDbContext db) =>
        RuleFor(x => x.Details.Name)
            .MustAsync(async (command, name, ct) => !await db.EventCategories.AnyAsync(c => c.Name == name.Trim() && c.Id != command.Id, ct))
            .WithName("Name").WithMessage("A category with this name already exists.");
}

internal sealed class SaveEventCategoryHandler(IAppDbContext db) : ICommandHandler<SaveEventCategory, DefaultIdType>
{
    public async ValueTask<DefaultIdType> Handle(SaveEventCategory command, CancellationToken cancellationToken)
    {
        var input = command.Details;
        EventCategory category;
        if (command.Id is { } id)
        {
            category = await db.EventCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw new NotFoundException("Category not found.");
            category.Apply(input.Name, input.Colour, input.SortOrder);
        }
        else
        {
            category = EventCategory.Create(input.Name, input.Colour, input.SortOrder);
            db.EventCategories.Add(category);
        }

        await db.SaveChangesAsync(cancellationToken);
        return category.Id;
    }
}

internal sealed class DeleteEventCategoryHandler(IAppDbContext db) : ICommandHandler<DeleteEventCategory>
{
    public async ValueTask<Unit> Handle(DeleteEventCategory command, CancellationToken cancellationToken)
    {
        var category = await db.EventCategories.FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken)
                       ?? throw new NotFoundException("Category not found.");
        if (await db.Events.AnyAsync(e => e.EventCategoryId == category.Id, cancellationToken))
            throw new BusinessRuleException($"Events are still in {category.Name}. Move them to another category first.");

        db.EventCategories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
