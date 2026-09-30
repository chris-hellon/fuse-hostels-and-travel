namespace Fuse.Contracts.Events;

public sealed record EventCategoryItem
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Hex, used for the category's events on the calendar.</summary>
    public string? Colour { get; init; }
    public int SortOrder { get; init; }
    /// <summary>Events in the category (a category with events can't be deleted).</summary>
    public int Events { get; init; }
}

/// <summary>All categories in display order (pick lists).</summary>
[Get("event-categories"), Permission(FuseAction.View, FuseResource.Events)]
public sealed record ListEventCategories : IQuery<IReadOnlyList<EventCategoryItem>>;

[Post("event-categories/search"), Permission(FuseAction.Search, FuseResource.Events)]
public sealed record SearchEventCategories : PagedQuery, IQuery<PagedResult<EventCategoryItem>>;

public sealed record EventCategoryInput
{
    public string Name { get; set; } = "";
    public string? Colour { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Creates (no <see cref="Id"/>) or updates a category.</summary>
[Put("event-categories"), Permission(FuseAction.Update, FuseResource.Events)]
public sealed record SaveEventCategory(DefaultIdType? Id, EventCategoryInput Details) : ICommand<DefaultIdType>;

/// <summary>Refused while events are in the category.</summary>
[Delete("event-categories/{Id}"), Permission(FuseAction.Delete, FuseResource.Events)]
public sealed record DeleteEventCategory(DefaultIdType Id) : ICommand;

public sealed class SaveEventCategoryValidator : AbstractValidator<SaveEventCategory>
{
    public SaveEventCategoryValidator()
    {
        RuleFor(x => x.Details.Name).NotEmpty().MaximumLength(128).WithName("Name");
        RuleFor(x => x.Details.Colour).Matches("^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$").When(x => !string.IsNullOrWhiteSpace(x.Details.Colour))
            .WithName("Colour").WithMessage("Colour must be a hex colour, e.g. #48bb78.");
        RuleFor(x => x.Details.SortOrder).GreaterThanOrEqualTo(0).WithName("Order");
    }
}
