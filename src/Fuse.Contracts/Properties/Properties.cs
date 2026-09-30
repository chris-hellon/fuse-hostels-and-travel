namespace Fuse.Contracts.Properties;

public sealed record PropertySummary
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    public string? LocationName { get; init; }
    public int Rooms { get; init; }
    public bool PublishToSite { get; init; }
}

[Post("properties/search"), Permission(FuseAction.Search, FuseResource.Properties)]
public sealed record SearchProperties : PagedQuery, IQuery<PagedResult<PropertySummary>>;

public sealed record PropertyInput : ISeoFields
{
    public string Name { get; set; } = "";
    public string? EmailAddress { get; set; }
    public bool PublishToSite { get; set; }
    public string AddressLine1 { get; set; } = "";
    public string? AddressLine2 { get; set; }
    public DefaultIdType? LocationId { get; set; }
    public string TelephoneNumber { get; set; } = "";
    public string? GoogleMapsPlaceId { get; set; }
    public string? ShortDescription { get; set; }
    public string Description { get; set; } = "";
    public string? SeoPageTitle { get; set; }
    public string? H1 { get; set; }
    public string? H2 { get; set; }
    public string? UrlSlug { get; set; }
    public string? MetaKeywords { get; set; }
    public string? MetaDescription { get; set; }
    public string? CustomSeoScripts { get; set; }
}

/// <summary>A room: an existing one (<see cref="Id"/>) or a new one. <see cref="ImageUrl"/> is the wanted image (a new upload or the current one).</summary>
public sealed record PropertyRoomEdit
{
    public DefaultIdType? Id { get; set; }
    public string Name { get; set; } = "";
    public string? ShortDescription { get; set; }
    public string Description { get; set; } = "";
    public string? ImageUrl { get; set; }
}

/// <summary>A set of directions ("From the airport"): a title and the text.</summary>
public sealed record PropertyDirectionEdit
{
    public DefaultIdType? Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
}

/// <summary>Set up by the Cloudbeds integration; shown, not edited here.</summary>
public sealed record CloudbedsSettings(string? PropertyId, string? Key, string? ApiKey);

public sealed record PropertyEditor
{
    public DefaultIdType Id { get; init; }
    public PropertyInput Details { get; init; } = new();
    public CloudbedsSettings Cloudbeds { get; init; } = new(null, null, null);
    public string? ImagePath { get; init; }
    public string? VideoPath { get; init; }
    public IReadOnlyList<GalleryImageItem> Gallery { get; init; } = [];
    public IReadOnlyList<PropertyRoomEdit> Rooms { get; init; } = [];
    public IReadOnlyList<PropertyDirectionEdit> Directions { get; init; } = [];
}

[Get("properties/{Id}"), Permission(FuseAction.View, FuseResource.Properties)]
public sealed record GetPropertyEditor(DefaultIdType Id) : IQuery<PropertyEditor>;

/// <summary>
/// Everything the wizard changed, saved together. Rooms, directions and the gallery are the complete
/// wanted lists (anything missing is removed). Media URLs come from <see cref="UploadPropertyMedia"/>.
/// </summary>
public sealed record PropertyChanges
{
    public PropertyInput Details { get; init; } = new();
    public List<PropertyRoomEdit> Rooms { get; init; } = [];
    public List<PropertyDirectionEdit> Directions { get; init; } = [];
    /// <summary>The wanted banner URL (current or newly uploaded).</summary>
    public string? Banner { get; init; }
    /// <summary>The wanted video URL (current, newly uploaded, or null for none).</summary>
    public string? Video { get; init; }
    public List<GalleryRef> Gallery { get; init; } = [];
}

[Post("properties"), Permission(FuseAction.Create, FuseResource.Properties)]
public sealed record CreateProperty(PropertyChanges Changes) : ICommand<DefaultIdType>;

[Put("properties/{Id}"), Permission(FuseAction.Update, FuseResource.Properties)]
public sealed record UpdateProperty(DefaultIdType Id, PropertyChanges Changes) : ICommand;

/// <summary>Refused while tours pick guests up from the property.</summary>
[Delete("properties/{Id}"), Permission(FuseAction.Delete, FuseResource.Properties)]
public sealed record DeleteProperty(DefaultIdType Id) : ICommand;

/// <summary>Stores an image or video the wizard attaches on Save, and returns its URL (keeps Save small).</summary>
[Post("properties/media"), Permission(FuseAction.Update, FuseResource.Properties)]
public sealed record UploadPropertyMedia(FileUpload File, bool IsVideo) : ICommand<string>;

public sealed class PropertyChangesValidator : AbstractValidator<PropertyChanges>
{
    public PropertyChangesValidator()
    {
        RuleFor(x => x.Details.Name).NotEmpty().MaximumLength(1024).WithName("Name");
        RuleFor(x => x.Details.AddressLine1).NotEmpty().WithName("Address line 1");
        RuleFor(x => x.Details.TelephoneNumber).NotEmpty().WithName("Telephone number");
        RuleFor(x => x.Details.Description).NotEmpty().WithName("Full description");
        RuleFor(x => x.Banner).NotEmpty().WithName("Banner image").WithMessage("Choose a banner image.");
        RuleForEach(x => x.Rooms).ChildRules(room =>
        {
            room.RuleFor(r => r.Name).NotEmpty().WithMessage("Each room needs a name.");
            room.RuleFor(r => r.Description).NotEmpty().WithMessage("Each room needs a full description.");
        }).WithName("Rooms");
        RuleFor(x => x.Rooms).Must(r => r.Select(x => x.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == r.Count)
            .WithName("Rooms").WithMessage("Two rooms have the same name.");
        RuleForEach(x => x.Directions).ChildRules(direction =>
        {
            direction.RuleFor(d => d.Title).NotEmpty().WithMessage("Each set of directions needs a title.");
            direction.RuleFor(d => d.Body).NotEmpty().WithMessage("Each set of directions needs some text.");
        }).WithName("Directions");
        RuleFor(x => x.Directions).Must(d => d.Select(x => x.Title.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == d.Count)
            .WithName("Directions").WithMessage("Two sets of directions have the same title.");
    }
}

public sealed class CreatePropertyValidator : AbstractValidator<CreateProperty>
{
    public CreatePropertyValidator() => RuleFor(x => x.Changes).SetValidator(new PropertyChangesValidator());
}

public sealed class UpdatePropertyValidator : AbstractValidator<UpdateProperty>
{
    public UpdatePropertyValidator() => RuleFor(x => x.Changes).SetValidator(new PropertyChangesValidator());
}
