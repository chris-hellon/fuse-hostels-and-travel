namespace Fuse.Contracts.TravelGuides;

public sealed record TravelGuideSummary
{
    public DefaultIdType Id { get; init; }
    public string Title { get; init; } = "";
    public string? UrlSlug { get; init; }
    public DateTime CreatedOn { get; init; }
    public DateTime? LastModifiedOn { get; init; }
}

[Post("travel-guides/search"), Permission(FuseAction.Search, FuseResource.TravelGuides)]
public sealed record SearchTravelGuides : PagedQuery, IQuery<PagedResult<TravelGuideSummary>>;

public sealed record TravelGuideInput : IMetaFields, ISlugField
{
    public string Title { get; set; } = "";
    public string SubTitle { get; set; } = "";
    /// <summary>Shown on the guide's card on the Travel Guides page.</summary>
    public string ShortDescription { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Empty: made from the title.</summary>
    public string? UrlSlug { get; set; }
    public string? MetaKeywords { get; set; }
    public string? MetaDescription { get; set; }
}

public sealed record TravelGuideEditor
{
    public DefaultIdType Id { get; init; }
    public TravelGuideInput Details { get; init; } = new();
    public string? ImagePath { get; init; }
    public List<GalleryImageItem> Gallery { get; init; } = [];
}

[Get("travel-guides/{Id}"), Permission(FuseAction.View, FuseResource.TravelGuides)]
public sealed record GetTravelGuideEditor(DefaultIdType Id) : IQuery<TravelGuideEditor>;

/// <summary>Everything the wizard saves at once. <see cref="Banner"/> and new slideshow images are URLs from <see cref="UploadTravelGuideImage"/>.</summary>
public sealed record TravelGuideChanges
{
    public TravelGuideInput Details { get; init; } = new();
    public string? Banner { get; init; }
    public List<GalleryRef> Gallery { get; init; } = [];
}

[Post("travel-guides"), Permission(FuseAction.Create, FuseResource.TravelGuides)]
public sealed record CreateTravelGuide(TravelGuideChanges Changes) : ICommand<DefaultIdType>;

[Put("travel-guides/{Id}"), Permission(FuseAction.Update, FuseResource.TravelGuides)]
public sealed record UpdateTravelGuide(DefaultIdType Id, TravelGuideChanges Changes) : ICommand;

[Delete("travel-guides/{Id}"), Permission(FuseAction.Delete, FuseResource.TravelGuides)]
public sealed record DeleteTravelGuide(DefaultIdType Id) : ICommand;

/// <summary>
/// Stores a picture and returns its URL: the banner and slideshow (attached on Save) and pictures in
/// the guide's text (in the HTML straight away, as legacy).
/// </summary>
[Post("travel-guides/images"), Permission(FuseAction.Update, FuseResource.TravelGuides)]
public sealed record UploadTravelGuideImage(FileUpload File) : ICommand<string>;

public sealed class TravelGuideChangesValidator : AbstractValidator<TravelGuideChanges>
{
    public TravelGuideChangesValidator()
    {
        RuleFor(x => x.Details.Title).NotEmpty().MaximumLength(1024).WithName("Title");
        RuleFor(x => x.Details.SubTitle).NotEmpty().MaximumLength(1024).WithName("Sub title");
        RuleFor(x => x.Details.ShortDescription).Must(HasText).WithName("Short description").WithMessage("Add a short description.");
        RuleFor(x => x.Details.Description).Must(HasText).WithName("Full description").WithMessage("Add the guide's text.");
        RuleFor(x => x.Details.UrlSlug).Matches("^[A-Za-z0-9]+(-[A-Za-z0-9]+)*$").When(x => !string.IsNullOrWhiteSpace(x.Details.UrlSlug))
            .WithName("URL slug").WithMessage("URL slug can only have letters, numbers and single dashes, e.g. best-bars-in-hoi-an.");
        RuleFor(x => x.Banner).NotEmpty().WithName("Banner image").WithMessage("Choose a banner image.");
    }

    /// <summary>Rich text counts only when it has visible text (an empty editor leaves "&lt;p&gt;&lt;br&gt;&lt;/p&gt;").</summary>
    private static bool HasText(string? html) =>
        !string.IsNullOrWhiteSpace(System.Text.RegularExpressions.Regex.Replace(html ?? "", "<[^>]+>|&nbsp;", " "));
}

public sealed class CreateTravelGuideValidator : AbstractValidator<CreateTravelGuide>
{
    public CreateTravelGuideValidator() => RuleFor(x => x.Changes).SetValidator(new TravelGuideChangesValidator());
}

public sealed class UpdateTravelGuideValidator : AbstractValidator<UpdateTravelGuide>
{
    public UpdateTravelGuideValidator() => RuleFor(x => x.Changes).SetValidator(new TravelGuideChangesValidator());
}
