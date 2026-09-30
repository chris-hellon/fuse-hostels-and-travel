using Fuse.Application.Common.Files;
using Fuse.Contracts.Locations;
using Fuse.Domain.Catalog.Destinations;

namespace Fuse.Application.Features.Locations;

internal static class LocationProjections
{
    public static IQueryable<LocationSummary> ToSummaries(this IQueryable<Destination> locations) =>
        locations.Select(d => new LocationSummary
        {
            Id = d.Id,
            Name = d.Name,
            ShortDescription = d.ShortDescription,
            ImagePath = d.ImagePath,
            ThumbnailImagePath = d.ThumbnailImagePath,
        });
}

internal sealed class ListLocationsHandler(IAppDbContext db) : IQueryHandler<ListLocations, IReadOnlyList<LocationSummary>>
{
    public async ValueTask<IReadOnlyList<LocationSummary>> Handle(ListLocations query, CancellationToken cancellationToken) =>
        await db.Destinations.AsNoTracking().OrderBy(d => d.Name).ToSummaries().ToListAsync(cancellationToken);
}

internal sealed class SearchLocationsHandler(IAppDbContext db) : IQueryHandler<SearchLocations, PagedResult<LocationSummary>>
{
    public ValueTask<PagedResult<LocationSummary>> Handle(SearchLocations query, CancellationToken cancellationToken)
    {
        var locations = db.Destinations.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
            locations = locations.Where(d => d.Name.Contains(query.Keyword));

        return new(locations.ToSummaries()
            .OrderBy(query.OrderBy, d => d.Name)
            .ToPagedResultAsync(query, cancellationToken));
    }
}

internal sealed class GetLocationHandler(IAppDbContext db) : IQueryHandler<GetLocation, LocationDetails>
{
    public async ValueTask<LocationDetails> Handle(GetLocation query, CancellationToken cancellationToken)
    {
        var d = await db.Destinations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
                ?? throw new NotFoundException("Location not found.");
        return new LocationDetails(d.Id, new LocationInput
        {
            Name = d.Name,
            ShortDescription = d.ShortDescription,
            Description = d.Description,
            Directions = d.Directions,
            GoogleMapsKey = d.GoogleMapsKey,
        }, string.IsNullOrEmpty(d.ImagePath) ? null : d.ImagePath);
    }
}

internal sealed class SaveLocationNameValidator : AbstractValidator<SaveLocation>
{
    public SaveLocationNameValidator(IAppDbContext db) =>
        RuleFor(x => x.Details.Name)
            .MustAsync(async (command, name, ct) => !await db.Destinations.AnyAsync(d => d.Name == name.Trim() && d.Id != command.Id, ct))
            .WithName("Name").WithMessage("A location with this name already exists.");
}

internal sealed class SaveLocationHandler(IAppDbContext db, IFileStore files) : ICommandHandler<SaveLocation, DefaultIdType>
{
    public async ValueTask<DefaultIdType> Handle(SaveLocation command, CancellationToken cancellationToken)
    {
        var input = command.Details;
        Destination location;
        if (command.Id is { } id)
        {
            location = await db.Destinations.FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
                          ?? throw new NotFoundException("Location not found.");
            location.Apply(input.Name, input.ShortDescription, input.Description, input.Directions, input.GoogleMapsKey);
        }
        else
        {
            location = Destination.Create(input.Name, input.ShortDescription, input.Description, input.Directions, input.GoogleMapsKey);
            db.Destinations.Add(location);
        }

        // The old file stays in storage, as legacy (it may be shared with the site).
        if (command.Image is not null || command.RemoveImage)
        {
            location.SetImage(command.Image is null ? null : await files.SaveAsync(command.Image, FileKind.Image, cancellationToken));
        }

        await db.SaveChangesAsync(cancellationToken);
        return location.Id;
    }
}

internal sealed class DeleteLocationHandler(IAppDbContext db) : ICommandHandler<DeleteLocation>
{
    public async ValueTask<Unit> Handle(DeleteLocation command, CancellationToken cancellationToken)
    {
        var location = await db.Destinations.FirstOrDefaultAsync(d => d.Id == command.Id, cancellationToken)
                          ?? throw new NotFoundException("Location not found.");

        // Only tours and properties that still exist (their links survive a soft delete).
        if (await db.TourDestinationLookups.AnyAsync(l => l.DestinationId == location.Id && db.Tours.Any(t => t.Id == l.TourId), cancellationToken)
            || await db.PropertyDestinationLookups.AnyAsync(l => l.DestinationId == location.Id && db.Properties.Any(p => p.Id == l.PropertyId), cancellationToken))
            throw new BusinessRuleException("Unable to delete this Location, as there are Tours or Properties associated with it.");

        db.Destinations.Remove(location);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
