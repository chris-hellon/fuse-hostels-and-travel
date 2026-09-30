namespace Fuse.Contracts.Tours;

// Init-only properties: handlers project with object initializers so EF can sort on them.
public sealed record TourCategoryItem
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    public string? ThumbnailImagePath { get; init; }
    /// <summary>Tours in the category (a category with tours can't be deleted).</summary>
    public int Tours { get; init; }
}

/// <summary>All categories, alphabetical (pick lists).</summary>
[Get("tour-categories"), Permission(FuseAction.View, FuseResource.Tours)]
public sealed record ListTourCategories : IQuery<IReadOnlyList<TourCategoryItem>>;

/// <summary>Back-office categories table.</summary>
[Post("tour-categories/search"), Permission(FuseAction.Search, FuseResource.Tours)]
public sealed record SearchTourCategories : PagedQuery, IQuery<PagedResult<TourCategoryItem>>;

public sealed record TourCategoryInput
{
    public string Name { get; set; } = "";
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
}

public sealed record TourCategoryDetails(DefaultIdType Id, TourCategoryInput Details, string? ImagePath);

[Get("tour-categories/{Id}"), Permission(FuseAction.View, FuseResource.Tours)]
public sealed record GetTourCategory(DefaultIdType Id) : IQuery<TourCategoryDetails>;

/// <summary>
/// Creates (no <see cref="Id"/>) or updates a category, image included, in one go (the wizard's
/// Save). <see cref="Image"/> replaces the image; <see cref="RemoveImage"/> clears it.
/// </summary>
[Put("tour-categories"), Permission(FuseAction.Update, FuseResource.Tours)]
public sealed record SaveTourCategory(DefaultIdType? Id, TourCategoryInput Details, FileUpload? Image, bool RemoveImage) : ICommand<DefaultIdType>;

/// <summary>Refused while tours are in the category.</summary>
[Delete("tour-categories/{Id}"), Permission(FuseAction.Delete, FuseResource.Tours)]
public sealed record DeleteTourCategory(DefaultIdType Id) : ICommand;

public sealed class SaveTourCategoryValidator : AbstractValidator<SaveTourCategory>
{
    public SaveTourCategoryValidator()
    {
        RuleFor(x => x.Details.Name).NotEmpty().MaximumLength(1024).WithName("Name");
        // As legacy: a new category needs an image (the website shows one per category).
        RuleFor(x => x.Image).NotNull().When(x => x.Id is null).WithName("Image").WithMessage("Choose an image for the new category.");
    }
}
