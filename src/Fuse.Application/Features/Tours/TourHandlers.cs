using Fuse.Domain.Identity;
using Fuse.Application.Common.Files;
using Fuse.Contracts.Tours;
using Fuse.Domain.Catalog.Tours;

namespace Fuse.Application.Features.Tours;

internal sealed class SearchToursHandler(IAppDbContext db) : IQueryHandler<SearchTours, PagedResult<TourSummary>>
{
    public ValueTask<PagedResult<TourSummary>> Handle(SearchTours query, CancellationToken cancellationToken)
    {
        var tours = db.Tours.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            tours = tours.Where(t => t.Name.Contains(query.Keyword));
        if (query.TourCategoryId is { } categoryId)
            tours = tours.Where(t => t.TourCategoryId == categoryId);
        if (query.LocationId is { } locationId)
            tours = tours.Where(t => t.TourDestinationLookups!.Any(d => d.DestinationId == locationId));

        var now = DateTime.UtcNow;
        return new(tours
            .Select(t => new TourSummary
            {
                Id = t.Id,
                Name = t.Name,
                CategoryName = t.TourCategory != null ? t.TourCategory.Name : null,
                PublishToSite = t.PublishToSite == true,
                ThumbnailImagePath = t.ThumbnailImagePath,
                FutureDates = t.TourDates!.Count(d => d.StartDate > now),
            })
            .OrderBy(query.OrderBy, t => t.Name)
            .ToPagedResultAsync(query, cancellationToken));
    }
}

internal sealed class GetTourEditorHandler(IAppDbContext db) : IQueryHandler<GetTourEditor, TourEditor>
{
    public async ValueTask<TourEditor> Handle(GetTourEditor query, CancellationToken cancellationToken)
    {
        var tour = await db.Tours.AsNoTracking().FirstOrDefaultAsync(t => t.Id == query.Id, cancellationToken)
                   ?? throw new NotFoundException("Tour not found.");

        var locations = await db.TourDestinationLookups.Where(l => l.TourId == tour.Id).Select(l => l.DestinationId).ToListAsync(cancellationToken);
        var pickups = await db.TourPickupLocations.Where(p => p.TourId == tour.Id).Select(p => p.PropertyId).ToListAsync(cancellationToken);
        var gallery = await db.TourImages.Where(i => i.TourId == tour.Id)
            .OrderBy(i => i.SortOrder)
            .Select(i => new TourImageItem(i.Id, i.ImagePath, i.ThumbnailImagePath, i.SortOrder ?? 0))
            .ToListAsync(cancellationToken);
        var prices = await db.TourPrices.AsNoTracking().Where(p => p.TourId == tour.Id).OrderByDescending(p => p.Price).ToListAsync(cancellationToken);
        var dates = await db.TourDates.AsNoTracking().Where(d => d.TourId == tour.Id).Upcoming().OrderBy(d => d.StartDate)
            .Select(d => new { d.Id, d.StartDate, d.EndDate, d.TourPriceId, d.PriceOverride, d.AvailableSpaces })
            .ToListAsync(cancellationToken);
        var booked = await db.BookedAsync([.. dates.Select(d => d.Id)], cancellationToken);

        return new TourEditor
        {
            Id = tour.Id,
            Capacity = tour.Capacity,
            ImagePath = string.IsNullOrEmpty(tour.ImagePath) ? null : tour.ImagePath,
            Gallery = gallery,
            Prices = [.. prices.Select(p => new TourPriceEdit { Key = p.Id, Details = p.ToInput() })],
            Dates = [.. dates.Select(d => new TourDateItem
            {
                Id = d.Id,
                StartDate = d.StartDate,
                EndDate = d.EndDate,
                PriceKey = d.TourPriceId,
                PriceOverride = d.PriceOverride,
                AvailableSpaces = d.AvailableSpaces,
                Booked = booked.Contains(d.Id),
            })],
            Details = new TourInput
            {
                Name = tour.Name,
                Description = tour.Description,
                ShortDescription = tour.ShortDescription,
                WhatsIncluded = tour.WhatsIncluded,
                WhatsNotIncluded = tour.WhatsNotIncluded,
                AdditionalInformation = tour.AdditionalInformation,
                ImportantInformation = tour.ImportantInformation,
                TelephoneNumber = tour.TelephoneNumber,
                DayDuration = tour.DayDuration,
                NightDuration = tour.NightDuration,
                HourDuration = tour.HourDuration,
                MinCapacity = tour.MinCapacity,
                MaxCapacity = tour.MaxCapacity == Tour.UnlimitedCapacity ? null : tour.MaxCapacity,
                PublishToSite = tour.PublishToSite == true,
                TourCategoryId = tour.TourCategoryId,
                LocationIds = locations,
                PickupPropertyIds = pickups,
                SupplierId = tour.SupplierId,
                WaiverRequired = tour.WaiverRequired == true,
                ShowBookingQRCode = tour.ShowBookingQRCode == true,
                ExcludeFromStaffSales = tour.ExcludeFromStaffSales == true,
                AdditionalGuestDetailsRequired = tour.AdditionalGuestDetailsRequired == true,
                BookingConfirmationEmailDetails = tour.BookingConfirmationEmailDetails,
                TermsAndConditions = tour.TermsAndConditions,
                CancellationPolicy = tour.CancellationPolicy,
                SeoPageTitle = tour.SeoPageTitle,
                H1 = tour.H1,
                H2 = tour.H2,
                UrlSlug = tour.UrlSlug,
                MetaKeywords = tour.MetaKeywords,
                MetaDescription = tour.MetaDescription,
                CustomSeoScripts = tour.CustomSeoScripts,
            },
        };
    }
}

/// <summary>Name uniqueness (needs the database, so it isn't in the contract's validator).</summary>
internal sealed class CreateTourNameValidator : AbstractValidator<CreateTour>
{
    public CreateTourNameValidator(IAppDbContext db) =>
        RuleFor(x => x.Changes.Details.Name)
            .MustAsync(async (name, ct) => !await db.Tours.AnyAsync(t => t.Name == name.Trim(), ct))
            .WithName("Name").WithMessage("A tour with this name already exists.");
}

internal sealed class UpdateTourNameValidator : AbstractValidator<UpdateTour>
{
    public UpdateTourNameValidator(IAppDbContext db) =>
        RuleFor(x => x.Changes.Details.Name)
            .MustAsync(async (command, name, ct) => !await db.Tours.AnyAsync(t => t.Name == name.Trim() && t.Id != command.Id, ct))
            .WithName("Name").WithMessage("A tour with this name already exists.");
}

internal sealed class CreateTourHandler(IAppDbContext db, IFileStore files) : ICommandHandler<CreateTour, DefaultIdType>
{
    public async ValueTask<DefaultIdType> Handle(CreateTour command, CancellationToken cancellationToken)
    {
        var tour = Tour.Create(command.Changes.Details.ToDomain());
        db.Tours.Add(tour);
        await new TourChangeSet(db, files, tour, previousName: null).ApplyAsync(command.Changes, capacityChange: 0, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return tour.Id;
    }
}

internal sealed class UpdateTourHandler(IAppDbContext db, IFileStore files) : ICommandHandler<UpdateTour>
{
    public async ValueTask<Unit> Handle(UpdateTour command, CancellationToken cancellationToken)
    {
        var tour = await db.FindTourAsync(command.Id, cancellationToken);
        var previousName = tour.Name;
        var capacityChange = tour.Apply(command.Changes.Details.ToDomain());
        await new TourChangeSet(db, files, tour, previousName).ApplyAsync(command.Changes, capacityChange, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class UploadTourImageHandler(IFileStore files) : ICommandHandler<UploadTourImage, string>
{
    public async ValueTask<string> Handle(UploadTourImage command, CancellationToken cancellationToken) =>
        await files.SaveAsync(command.File, FileKind.Image, cancellationToken);
}

internal static class TourLinks
{
    /// <summary>Adds the selected location/pickup links that don't exist and removes the rest.</summary>
    public static void Sync(IAppDbContext db, DefaultIdType tourId, TourInput input,
        IReadOnlyList<TourDestinationLookup> locations, IReadOnlyList<TourPickupLocation> pickups)
    {
        var wantedLocations = input.LocationIds.ToHashSet();
        db.TourDestinationLookups.RemoveRange(locations.Where(d => !wantedLocations.Contains(d.DestinationId)));
        foreach (var id in wantedLocations.Except(locations.Select(d => d.DestinationId)))
            db.TourDestinationLookups.Add(new TourDestinationLookup(tourId, id));

        var wantedPickups = input.PickupPropertyIds.ToHashSet();
        db.TourPickupLocations.RemoveRange(pickups.Where(p => !wantedPickups.Contains(p.PropertyId)));
        foreach (var id in wantedPickups.Except(pickups.Select(p => p.PropertyId)))
            db.TourPickupLocations.Add(new TourPickupLocation(tourId, id));
    }
}

internal sealed class DeleteTourHandler(IAppDbContext db) : ICommandHandler<DeleteTour>
{
    public async ValueTask<Unit> Handle(DeleteTour command, CancellationToken cancellationToken)
    {
        var tour = await db.FindTourAsync(command.Id, cancellationToken);

        if (await db.BookingItems.AnyAsync(i => i.TourId == tour.Id && i.StartDate >= DateTime.UtcNow, cancellationToken))
            throw new BusinessRuleException("This tour has upcoming bookings, so it can't be deleted. Unpublish it instead.");

        db.Tours.Remove(tour);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class ListSuppliersHandler(IAppDbContext db) : IQueryHandler<ListSuppliers, IReadOnlyList<LookupItem>>
{
    public async ValueTask<IReadOnlyList<LookupItem>> Handle(ListSuppliers query, CancellationToken cancellationToken)
    {
        // In steps, each on an index: the role, its (few) members, then just those users. One join across every user
        // (tens of thousands of guests) took seconds on the live database.
        var roleIds = await db.Roles.Where(r => r.Name == FuseRoles.Supplier).Select(r => r.Id).ToListAsync(cancellationToken);
        var memberIds = await db.UserRoles.Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.UserId).Distinct().ToListAsync(cancellationToken);
        if (memberIds.Count == 0)
            return [];
        var suppliers = await db.Users.AsNoTracking().Where(u => memberIds.Contains(u.Id) && u.IsActive)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.FullName, u.Email, u.UserName })
            .ToListAsync(cancellationToken);
        return
        [
            .. suppliers
                .Select(u => new LookupItem(u.Id, PersonNames.Display(u.FirstName, u.LastName, u.FullName) is { Length: > 0 } name ? name : u.Email ?? u.UserName!))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }
}

internal sealed class ListPickupPropertiesHandler(IAppDbContext db) : IQueryHandler<ListPickupProperties, IReadOnlyList<LookupItem>>
{
    public async ValueTask<IReadOnlyList<LookupItem>> Handle(ListPickupProperties query, CancellationToken cancellationToken)
    {
        // Ids are formatted in .NET (lower case), not by SQL Server (upper case), so they match Guid.ToString() in the UI.
        var properties = await db.Properties.OrderBy(p => p.Name).Select(p => new { p.Id, p.Name }).ToListAsync(cancellationToken);
        return [.. properties.Select(p => new LookupItem(p.Id.ToString(), p.Name))];
    }
}
