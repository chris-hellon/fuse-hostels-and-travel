namespace Fuse.Contracts.Galleries;

public sealed record GallerySummary
{
    public DefaultIdType Id { get; init; }
    public string Title { get; init; } = "";
    public int Images { get; init; }
    public DateTime CreatedOn { get; init; }
}

[Post("galleries/search"), Permission(FuseAction.Search, FuseResource.Gallery)]
public sealed record SearchGalleries : PagedQuery, IQuery<PagedResult<GallerySummary>>;

public sealed record GalleryInput : IMetaFields
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? MetaKeywords { get; set; }
    public string? MetaDescription { get; set; }
}

public sealed record GalleryEditor(DefaultIdType Id, GalleryInput Details, IReadOnlyList<GalleryImageItem> Images);

[Get("galleries/{Id}"), Permission(FuseAction.View, FuseResource.Gallery)]
public sealed record GetGalleryEditor(DefaultIdType Id) : IQuery<GalleryEditor>;

/// <summary>Creates (no <see cref="Id"/>) or updates a gallery with its images in order (new ones are URLs from <see cref="UploadGalleryImage"/>).</summary>
[Put("galleries"), Permission(FuseAction.Update, FuseResource.Gallery)]
public sealed record SaveGallery(DefaultIdType? Id, GalleryInput Details, IReadOnlyList<GalleryRef> Images) : ICommand<DefaultIdType>;

[Delete("galleries/{Id}"), Permission(FuseAction.Delete, FuseResource.Gallery)]
public sealed record DeleteGallery(DefaultIdType Id) : ICommand;

/// <summary>Stores a picture the wizard attaches on Save, and returns its URL.</summary>
[Post("galleries/images"), Permission(FuseAction.Update, FuseResource.Gallery)]
public sealed record UploadGalleryImage(FileUpload File) : ICommand<string>;

public sealed class SaveGalleryValidator : AbstractValidator<SaveGallery>
{
    public SaveGalleryValidator()
    {
        RuleFor(x => x.Details.Title).NotEmpty().MaximumLength(1024).WithName("Title");
        RuleFor(x => x.Details.Description).Must(d => !string.IsNullOrWhiteSpace(System.Text.RegularExpressions.Regex.Replace(d ?? "", "<[^>]+>|&nbsp;", " ")))
            .WithName("Description").WithMessage("Add a description.");
        RuleFor(x => x.Images).NotEmpty().WithName("Images").WithMessage("Add at least one image.");
    }
}
