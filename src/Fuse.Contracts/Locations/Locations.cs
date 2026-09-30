namespace Fuse.Contracts.Locations;

// Init-only properties, not a positional record: handlers project with object initializers so
// EF can still translate sorting on the projected shape (it can't see through constructors).
public sealed record LocationSummary
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    public string? ShortDescription { get; init; }
    public string? ImagePath { get; init; }
    public string? ThumbnailImagePath { get; init; }
}

/// <summary>All locations, alphabetical (public site menus and filters).</summary>
[Get("locations"), Public]
public sealed record ListLocations : IQuery<IReadOnlyList<LocationSummary>>;

/// <summary>Back-office locations table.</summary>
[Post("locations/search"), Permission(FuseAction.Search, FuseResource.Locations)]
public sealed record SearchLocations : PagedQuery, IQuery<PagedResult<LocationSummary>>;

public sealed record LocationInput
{
    public string Name { get; set; } = "";
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public string? Directions { get; set; }
    public string? GoogleMapsKey { get; set; }
}

public sealed record LocationDetails(DefaultIdType Id, LocationInput Details, string? ImagePath);

[Get("locations/{Id}"), Permission(FuseAction.View, FuseResource.Locations)]
public sealed record GetLocation(DefaultIdType Id) : IQuery<LocationDetails>;

/// <summary>
/// Creates (no <see cref="Id"/>) or updates a location. <see cref="Image"/> replaces the banner;
/// <see cref="RemoveImage"/> clears it. Everything saves together (the legacy wizard's behaviour).
/// </summary>
[Put("locations"), Permission(FuseAction.Update, FuseResource.Locations)]
public sealed record SaveLocation(DefaultIdType? Id, LocationInput Details, FileUpload? Image, bool RemoveImage) : ICommand<DefaultIdType>;

/// <summary>Refused while tours or properties are in the location.</summary>
[Delete("locations/{Id}"), Permission(FuseAction.Delete, FuseResource.Locations)]
public sealed record DeleteLocation(DefaultIdType Id) : ICommand;

public sealed class SaveLocationValidator : AbstractValidator<SaveLocation>
{
    public SaveLocationValidator()
    {
        RuleFor(x => x.Details.Name).NotEmpty().MaximumLength(1024).WithName("Name");
    }
}
