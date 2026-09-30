using Fuse.Application.Common.Files;
using Fuse.Contracts.Properties;
using Fuse.Domain.Catalog.Properties;

namespace Fuse.Application.Features.Properties;

internal sealed class SearchPropertiesHandler(IAppDbContext db) : IQueryHandler<SearchProperties, PagedResult<PropertySummary>>
{
    public ValueTask<PagedResult<PropertySummary>> Handle(SearchProperties query, CancellationToken cancellationToken)
    {
        var properties = db.Properties.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            properties = properties.Where(p => p.Name.Contains(query.Keyword));

        return new(properties
            .Select(p => new PropertySummary
            {
                Id = p.Id,
                Name = p.Name,
                LocationName = db.PropertyDestinationLookups.Where(l => l.PropertyId == p.Id).Select(l => l.Destination.Name).FirstOrDefault(),
                Rooms = db.PropertyRooms.Count(r => r.PropertyId == p.Id),
                PublishToSite = p.PublishToSite == true,
            })
            .OrderBy(query.OrderBy, p => p.Name)
            .ToPagedResultAsync(query, cancellationToken));
    }
}

internal sealed class GetPropertyEditorHandler(IAppDbContext db) : IQueryHandler<GetPropertyEditor, PropertyEditor>
{
    public async ValueTask<PropertyEditor> Handle(GetPropertyEditor query, CancellationToken cancellationToken)
    {
        var p = await db.Properties.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
                ?? throw new NotFoundException("Property not found.");
        var locationId = await db.PropertyDestinationLookups.Where(l => l.PropertyId == p.Id).Select(l => (DefaultIdType?)l.DestinationId).FirstOrDefaultAsync(cancellationToken);
        var rooms = await db.PropertyRooms.AsNoTracking().Where(r => r.PropertyId == p.Id).OrderBy(r => r.Name)
            .Select(r => new PropertyRoomEdit { Id = r.Id, Name = r.Name, ShortDescription = r.ShortDescription, Description = r.Description, ImageUrl = r.ImagePath })
            .ToListAsync(cancellationToken);
        var directions = await db.PropertyDirections.AsNoTracking().Where(d => d.PropertyId == p.Id).OrderBy(d => d.CreatedOn)
            .Select(d => new PropertyDirectionEdit
            {
                Id = d.Id,
                Title = d.Title,
                Body = db.PropertyDirectionContents.Where(c => c.PropertyDirectionId == d.Id).OrderBy(c => c.CreatedOn).Select(c => c.Body).FirstOrDefault() ?? "",
            })
            .ToListAsync(cancellationToken);
        var gallery = await db.PropertyImages.AsNoTracking().Where(i => i.PropertyId == p.Id).OrderBy(i => i.SortOrder)
            .Select(i => new GalleryImageItem(i.Id, i.ImagePath, i.ThumbnailImagePath))
            .ToListAsync(cancellationToken);

        return new PropertyEditor
        {
            Id = p.Id,
            ImagePath = string.IsNullOrEmpty(p.ImagePath) ? null : p.ImagePath,
            VideoPath = string.IsNullOrEmpty(p.VideoPath) ? null : p.VideoPath,
            Cloudbeds = new CloudbedsSettings(p.CloudbedsPropertyId, p.CloudbedsKey, p.CloudbedsApiKey),
            Gallery = gallery,
            Rooms = [.. rooms.Select(r => r with { ImageUrl = string.IsNullOrEmpty(r.ImageUrl) ? null : r.ImageUrl })],
            Directions = directions,
            Details = new PropertyInput
            {
                Name = p.Name,
                EmailAddress = p.EmailAddress,
                PublishToSite = p.PublishToSite == true,
                AddressLine1 = p.AddressLine1,
                AddressLine2 = p.AddressLine2,
                LocationId = locationId,
                TelephoneNumber = p.TelephoneNumber,
                GoogleMapsPlaceId = p.GoogleMapsPlaceId,
                ShortDescription = p.ShortDescription,
                Description = p.Description,
                SeoPageTitle = p.SeoPageTitle,
                H1 = p.H1,
                H2 = p.H2,
                UrlSlug = p.UrlSlug,
                MetaKeywords = p.MetaKeywords,
                MetaDescription = p.MetaDescription,
                CustomSeoScripts = p.CustomSeoScripts,
            },
        };
    }
}

internal sealed class CreatePropertyNameValidator : AbstractValidator<CreateProperty>
{
    public CreatePropertyNameValidator(IAppDbContext db) =>
        RuleFor(x => x.Changes.Details.Name)
            .MustAsync(async (name, ct) => !await db.Properties.AnyAsync(p => p.Name == name.Trim(), ct))
            .WithName("Name").WithMessage("A property with this name already exists.");
}

internal sealed class UpdatePropertyNameValidator : AbstractValidator<UpdateProperty>
{
    public UpdatePropertyNameValidator(IAppDbContext db) =>
        RuleFor(x => x.Changes.Details.Name)
            .MustAsync(async (command, name, ct) => !await db.Properties.AnyAsync(p => p.Name == name.Trim() && p.Id != command.Id, ct))
            .WithName("Name").WithMessage("A property with this name already exists.");
}

internal sealed class CreatePropertyHandler(IAppDbContext db, IFileStore files) : ICommandHandler<CreateProperty, DefaultIdType>
{
    public async ValueTask<DefaultIdType> Handle(CreateProperty command, CancellationToken cancellationToken)
    {
        var property = Property.Create(command.Changes.Details.ToDomain());
        db.Properties.Add(property);
        await new PropertyChangeSet(db, files, property, previousName: null).ApplyAsync(command.Changes, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return property.Id;
    }
}

internal sealed class UpdatePropertyHandler(IAppDbContext db, IFileStore files) : ICommandHandler<UpdateProperty>
{
    public async ValueTask<Unit> Handle(UpdateProperty command, CancellationToken cancellationToken)
    {
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
                       ?? throw new NotFoundException("Property not found.");
        var previousName = property.Name;
        property.Apply(command.Changes.Details.ToDomain());
        await new PropertyChangeSet(db, files, property, previousName).ApplyAsync(command.Changes, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class DeletePropertyHandler(IAppDbContext db) : ICommandHandler<DeleteProperty>
{
    public async ValueTask<Unit> Handle(DeleteProperty command, CancellationToken cancellationToken)
    {
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
                       ?? throw new NotFoundException("Property not found.");
        // Only tours that still exist: deleting a tour is a soft delete that leaves its pickup links behind.
        if (await db.TourPickupLocations.AnyAsync(p => p.PropertyId == property.Id && db.Tours.Any(t => t.Id == p.TourId), cancellationToken))
            throw new BusinessRuleException($"Tours still pick guests up from {property.Name}. Remove it from those tours first.");

        db.Properties.Remove(property);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class UploadPropertyMediaHandler(IFileStore files) : ICommandHandler<UploadPropertyMedia, string>
{
    public async ValueTask<string> Handle(UploadPropertyMedia command, CancellationToken cancellationToken) =>
        await files.SaveAsync(command.File, command.IsVideo ? FileKind.Video : FileKind.Image, cancellationToken);
}

internal static class PropertyMapping
{
    public static PropertyDetails ToDomain(this PropertyInput input) => new()
    {
        Name = input.Name,
        EmailAddress = input.EmailAddress,
        PublishToSite = input.PublishToSite,
        AddressLine1 = input.AddressLine1,
        AddressLine2 = input.AddressLine2,
        TelephoneNumber = input.TelephoneNumber,
        GoogleMapsPlaceId = input.GoogleMapsPlaceId,
        ShortDescription = input.ShortDescription,
        Description = input.Description,
        SeoPageTitle = input.SeoPageTitle,
        H1 = input.H1,
        H2 = input.H2,
        UrlSlug = input.UrlSlug,
        MetaKeywords = input.MetaKeywords,
        MetaDescription = input.MetaDescription,
        CustomSeoScripts = input.CustomSeoScripts,
    };

    /// <summary>The CMS page that carries a property's SEO on the public site ("Hostels - {Name}", as before).</summary>
    public static string PageTitle(string propertyName) => $"Hostels - {propertyName}";
}
