using Fuse.Application.Common.Time;
using Fuse.Application.Features.Packages;
using Fuse.Domain.Identity;

namespace Fuse.Application.Features.PublicApi;

// The public booking API's reads (legacy /api/tours, /api/packages, /api/pack-incentives, /api/users and
// /api/bookings), returning legacy's response shapes. Callers are the API's own endpoints, which have already
// checked the caller's token: these requests carry no [Permission] of their own.

public sealed record ListApiTourCategories : IQuery<List<TourCategoryApiDto>>;

public sealed record ListApiTourLocations : IQuery<List<TourLocationApiDto>>;

public sealed record SearchApiTours(SearchToursApiRequest Request) : IQuery<PaginationResponse<TourSearchResultDto>>;

/// <summary>Null when there's no such tour.</summary>
public sealed record GetApiTourBookingOptions(DefaultIdType Id, DateTime From, DateTime To, int? MinSpaces) : IQuery<TourBookingOptionsDto?>;

public sealed record SearchApiPackages(SearchPackagesApiRequest Request) : IQuery<PaginationResponse<PackageSearchResultDto>>;

/// <summary>Null when there's no such package.</summary>
public sealed record GetApiPackage(DefaultIdType Id) : IQuery<PackageDetailsApiDto?>;

/// <summary>The package the basket exactly makes up; null when it's none.</summary>
public sealed record MatchApiPackage(IReadOnlyCollection<DefaultIdType> TourIds) : IQuery<PackageDetailsApiDto?>;

public sealed record ListApiPackIncentives : IQuery<List<PackIncentiveApiDto>>;

/// <summary>The best pack the basket qualifies for; null when it's none.</summary>
public sealed record MatchApiPackIncentive(IReadOnlyCollection<DefaultIdType> TourIds) : IQuery<PackIncentiveApiDto?>;

public sealed record SearchApiUsers(SearchUsersApiRequest Request) : IQuery<PaginationResponse<UserDetailsDto>>;

/// <summary>Null when there's no such user.</summary>
public sealed record GetApiUser(string Id) : IQuery<UserDetailsDto?>;

public sealed record SearchApiBookings(SearchBookingsApiRequest Request) : IQuery<PaginationResponse<BookingListDto>>;

/// <summary>Null when there's no such booking.</summary>
public sealed record GetApiBooking(DefaultIdType Id) : IQuery<BookingDetailsDto?>;

// Tours ----------------------------------------------------------------------------------------------------------

internal sealed class ListApiTourCategoriesHandler(IAppDbContext db) : IQueryHandler<ListApiTourCategories, List<TourCategoryApiDto>>
{
    public async ValueTask<List<TourCategoryApiDto>> Handle(ListApiTourCategories query, CancellationToken cancellationToken) =>
        await db.TourCategories.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new TourCategoryApiDto { Id = c.Id, Name = c.Name })
            .ToListAsync(cancellationToken);
}

internal sealed class ListApiTourLocationsHandler(IAppDbContext db) : IQueryHandler<ListApiTourLocations, List<TourLocationApiDto>>
{
    public async ValueTask<List<TourLocationApiDto>> Handle(ListApiTourLocations query, CancellationToken cancellationToken) =>
        await db.Destinations.AsNoTracking().OrderBy(d => d.Name)
            .Select(d => new TourLocationApiDto { Id = d.Id, Name = d.Name })
            .ToListAsync(cancellationToken);
}

internal sealed class SearchApiToursHandler(IAppDbContext db) : IQueryHandler<SearchApiTours, PaginationResponse<TourSearchResultDto>>
{
    public async ValueTask<PaginationResponse<TourSearchResultDto>> Handle(SearchApiTours query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var tours = db.Tours.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Name))
            tours = tours.Where(t => t.Name.Contains(request.Name));
        if (request.TourCategoryId is { } categoryId)
            tours = tours.Where(t => t.TourCategoryId == categoryId);
        if (request.LocationId is { } locationId)
            tours = tours.Where(t => t.TourDestinationLookups!.Any(d => d.DestinationId == locationId));

        var count = await tours.CountAsync(cancellationToken);
        var page = await tours.OrderBy(t => t.Name)
            .Skip((Math.Max(request.PageNumber, 1) - 1) * request.PageSize).Take(request.PageSize)
            .Select(t => new TourSearchResultDto
            {
                Id = t.Id,
                Name = t.Name,
                ShortDescription = t.ShortDescription,
                ThumbnailImagePath = t.ThumbnailImagePath,
                MinCapacity = t.MinCapacity,
                MaxCapacity = t.MaxCapacity,
                TourCategoryId = t.TourCategoryId,
                WaiverRequired = t.WaiverRequired == true,
                PubliclyBookable = t.PublishToSite == true,
            })
            .ToListAsync(cancellationToken);

        // Each tour's locations, in one query.
        var ids = page.Select(t => t.Id).ToList();
        var locations = await db.TourDestinationLookups.AsNoTracking().Where(l => ids.Contains(l.TourId))
            .Select(l => new { l.TourId, Location = new TourLocationApiDto { Id = l.DestinationId, Name = l.Destination.Name } })
            .ToListAsync(cancellationToken);
        foreach (var tour in page)
            tour.Locations = [.. locations.Where(l => l.TourId == tour.Id).Select(l => l.Location)];

        return new PaginationResponse<TourSearchResultDto>(page, count, request.PageNumber, request.PageSize);
    }
}

internal sealed class GetApiTourBookingOptionsHandler(IAppDbContext db, IBusinessClock clock) : IQueryHandler<GetApiTourBookingOptions, TourBookingOptionsDto?>
{
    public async ValueTask<TourBookingOptionsDto?> Handle(GetApiTourBookingOptions query, CancellationToken cancellationToken)
    {
        var tour = await db.Tours.AsNoTracking().Where(t => t.Id == query.Id)
            .Select(t => new TourBookingOptionsDto
            {
                Id = t.Id,
                Name = t.Name,
                ShortDescription = t.ShortDescription,
                WaiverRequired = t.WaiverRequired == true,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (tour is null)
            return null;

        // As legacy: only departures still to come, within the window.
        var now = clock.Now;
        tour.Dates = await db.TourDates.AsNoTracking()
            .Where(d => d.TourId == query.Id && d.StartDate > now && d.StartDate >= query.From && d.StartDate <= query.To)
            .Where(d => query.MinSpaces == null || d.AvailableSpaces >= query.MinSpaces)
            .OrderBy(d => d.StartDate)
            .Select(d => new TourDateOptionDto
            {
                Id = d.Id,
                StartDate = d.StartDate,
                EndDate = d.EndDate,
                AvailableSpaces = d.AvailableSpaces,
                Price = d.PriceOverride ?? (d.TourPrice != null ? d.TourPrice.Price : null),
                PriceTitle = d.TourPrice != null ? d.TourPrice.Title : null,
            })
            .ToListAsync(cancellationToken);
        tour.PickupLocations = await db.TourPickupLocations.AsNoTracking().Where(p => p.TourId == query.Id)
            .OrderBy(p => p.Property.Name)
            .Select(p => new TourPickupLocationOptionDto { PropertyId = p.PropertyId, PropertyName = p.Property.Name })
            .ToListAsync(cancellationToken);
        tour.Locations = await db.TourDestinationLookups.AsNoTracking().Where(l => l.TourId == query.Id)
            .Select(l => new TourLocationApiDto { Id = l.DestinationId, Name = l.Destination.Name })
            .ToListAsync(cancellationToken);
        return tour;
    }
}

// Packages -------------------------------------------------------------------------------------------------------

internal static class ApiPackages
{
    public static async Task<List<PackageDetailsApiDto>> DetailsAsync(IAppDbContext db, IQueryable<Domain.Catalog.Packages.Package> packages,
        CancellationToken cancellationToken)
    {
        var loaded = await packages.AsNoTracking().AsSplitQuery()
            .Include(p => p.TourCategory)
            .Include(p => p.PackageTiers)
            .Include(p => p.PackageItems!).ThenInclude(i => i.PackageItemPrices)
            .Include(p => p.PackageItems!).ThenInclude(i => i.Tour)
            .ToListAsync(cancellationToken);
        var totals = await PackagePricing.TierTotalsAsync(db, [.. loaded.Select(p => p.Id)], cancellationToken);
        return [.. loaded.Select(p => ToDetails(p, totals.GetValueOrDefault(p.Id)))];
    }

    private static PackageDetailsApiDto ToDetails(Domain.Catalog.Packages.Package package, List<decimal>? totals)
    {
        var tiers = (package.PackageTiers ?? []).OrderBy(t => t.SortOrder).ToList();
        return new PackageDetailsApiDto
        {
            Id = package.Id,
            Name = package.Name,
            Description = package.Description,
            ShortDescription = package.ShortDescription,
            ImagePath = package.ImagePath,
            PriceLabel = package.PriceLabel,
            TourCategoryId = package.TourCategoryId,
            TourCategoryName = package.TourCategory?.Name,
            Tiers = [.. tiers.Select(t => new PackageTierApiDto { Id = t.Id, Title = t.Title, Total = Total(package, t.Id) })],
            Tours =
            [
                .. (package.PackageItems ?? []).OrderBy(i => i.SortOrder).Select(i => new PackageTourApiDto
                {
                    TourId = i.TourId,
                    TourName = i.Tour?.Name,
                    Quantity = i.Quantity,
                    Prices = [.. (i.PackageItemPrices ?? []).Select(p => new PackageTourTierPriceApiDto { TierId = p.PackageTierId, Price = p.Price })],
                }),
            ],
        };
    }

    /// <summary>A tier's total per guest: each tour's price in the tier times its quantity.</summary>
    public static decimal Total(Domain.Catalog.Packages.Package package, DefaultIdType tierId) =>
        (package.PackageItems ?? []).Sum(i => (i.PackageItemPrices ?? []).Where(p => p.PackageTierId == tierId).Select(p => p.Price).FirstOrDefault() * i.Quantity);
}

internal sealed class SearchApiPackagesHandler(IAppDbContext db) : IQueryHandler<SearchApiPackages, PaginationResponse<PackageSearchResultDto>>
{
    public async ValueTask<PaginationResponse<PackageSearchResultDto>> Handle(SearchApiPackages query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var packages = db.Packages.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Name))
            packages = packages.Where(p => p.Name.Contains(request.Name));
        if (request.TourCategoryId is { } categoryId)
            packages = packages.Where(p => p.TourCategoryId == categoryId);

        var count = await packages.CountAsync(cancellationToken);
        var ids = await packages.OrderBy(p => p.Name)
            .Skip((Math.Max(request.PageNumber, 1) - 1) * request.PageSize).Take(request.PageSize)
            .Select(p => p.Id).ToListAsync(cancellationToken);
        var details = (await ApiPackages.DetailsAsync(db, db.Packages.Where(p => ids.Contains(p.Id)), cancellationToken)).ToDictionary(p => p.Id);
        var thumbnails = await db.Packages.AsNoTracking().Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.ThumbnailImagePath, cancellationToken);

        var page = ids.Where(details.ContainsKey).Select(id =>
        {
            var p = details[id];
            return new PackageSearchResultDto
            {
                Id = p.Id,
                Name = p.Name,
                ShortDescription = p.ShortDescription,
                ThumbnailImagePath = thumbnails.GetValueOrDefault(id),
                PriceLabel = p.PriceLabel,
                TourCategoryId = p.TourCategoryId,
                TourCategoryName = p.TourCategoryName,
                PriceFrom = p.Tiers.Count > 0 ? p.Tiers.Min(t => t.Total) : null,
                PriceTo = p.Tiers.Count > 0 ? p.Tiers.Max(t => t.Total) : null,
                Tiers = p.Tiers,
            };
        }).ToList();
        return new PaginationResponse<PackageSearchResultDto>(page, count, request.PageNumber, request.PageSize);
    }
}

internal sealed class GetApiPackageHandler(IAppDbContext db) : IQueryHandler<GetApiPackage, PackageDetailsApiDto?>
{
    public async ValueTask<PackageDetailsApiDto?> Handle(GetApiPackage query, CancellationToken cancellationToken) =>
        (await ApiPackages.DetailsAsync(db, db.Packages.Where(p => p.Id == query.Id), cancellationToken)).FirstOrDefault();
}

internal sealed class MatchApiPackageHandler(IAppDbContext db) : IQueryHandler<MatchApiPackage, PackageDetailsApiDto?>
{
    public async ValueTask<PackageDetailsApiDto?> Handle(MatchApiPackage query, CancellationToken cancellationToken)
    {
        var basket = query.TourIds.Where(id => id != DefaultIdType.Empty).ToHashSet();
        // A package is at least two tours; a single loose tour can never be a package.
        if (basket.Count < 2)
            return null;

        var packages = await ApiPackages.DetailsAsync(db, db.Packages.OrderBy(p => p.Name), cancellationToken);
        return packages.FirstOrDefault(p =>
        {
            var tourIds = p.Tours.Select(t => t.TourId).ToHashSet();
            return tourIds.Count >= 2 && tourIds.SetEquals(basket);
        });
    }
}

// Pack incentives ------------------------------------------------------------------------------------------------

internal static class ApiPacks
{
    public static async Task<List<PackIncentiveApiDto>> ActiveAsync(IAppDbContext db, CancellationToken cancellationToken)
    {
        var packs = await db.PackIncentives.AsNoTracking().AsSplitQuery()
            .Where(p => p.IsActive)
            .Include(p => p.Tours!).ThenInclude(t => t.Tour)
            .Include(p => p.Tiers)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
        return
        [
            .. packs.Select(p => new PackIncentiveApiDto
            {
                Id = p.Id,
                Name = p.Name,
                MinimumProductCount = p.MinimumProductCount,
                CommissionAmount = p.CommissionAmount,
                IgnoresMinimumPax = p.IgnoresMinimumPax,
                Tours = [.. (p.Tours ?? []).Select(t => new PackIncentiveTourApiDto { TourId = t.TourId, TourName = t.Tour?.Name })],
                Tiers = [.. (p.Tiers ?? []).OrderBy(t => t.SortOrder).Select(t => new PackIncentiveTierApiDto { Id = t.Id, Title = t.Title, Price = t.Price })],
            }),
        ];
    }
}

internal sealed class ListApiPackIncentivesHandler(IAppDbContext db) : IQueryHandler<ListApiPackIncentives, List<PackIncentiveApiDto>>
{
    public async ValueTask<List<PackIncentiveApiDto>> Handle(ListApiPackIncentives query, CancellationToken cancellationToken) =>
        await ApiPacks.ActiveAsync(db, cancellationToken);
}

internal sealed class MatchApiPackIncentiveHandler(IAppDbContext db) : IQueryHandler<MatchApiPackIncentive, PackIncentiveApiDto?>
{
    public async ValueTask<PackIncentiveApiDto?> Handle(MatchApiPackIncentive query, CancellationToken cancellationToken)
    {
        var basket = query.TourIds.Where(id => id != DefaultIdType.Empty).ToHashSet();
        if (basket.Count == 0)
            return null;

        // The best pack the basket qualifies for: the highest threshold met (so Pack 4 wins over Pack 3).
        return (await ApiPacks.ActiveAsync(db, cancellationToken))
            .Where(p => p.MinimumProductCount > 0 && basket.Count(id => p.Tours.Any(t => t.TourId == id)) >= p.MinimumProductCount)
            .OrderByDescending(p => p.MinimumProductCount)
            .FirstOrDefault();
    }
}

// Users ----------------------------------------------------------------------------------------------------------

internal sealed class SearchApiUsersHandler(IAppDbContext db, ITenantContext tenant) : IQueryHandler<SearchApiUsers, PaginationResponse<UserDetailsDto>>
{
    public async ValueTask<PaginationResponse<UserDetailsDto>> Handle(SearchApiUsers query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        // Legacy's own search: the SearchUsers procedure, sorted as asked ("FirstName Ascending" by default).
        var rows = await db.QueryProcedureAsync<UserDetailsDto>("SearchUsers", new
        {
            tenant.TenantId,
            request.Role,
            Search = request.Keyword,
            request.PageNumber,
            request.PageSize,
            OrderBy = request.OrderBy is { Length: > 0 } orderBy ? orderBy[0] : "FirstName Ascending",
            BookingItemId = (Guid?)null,
        }, cancellationToken);

        return new PaginationResponse<UserDetailsDto>([.. rows], rows.Count > 0 ? rows[0].TotalUsers : 0, request.PageNumber, request.PageSize);
    }
}

internal sealed class GetApiUserHandler(IAppDbContext db) : IQueryHandler<GetApiUser, UserDetailsDto?>
{
    public async ValueTask<UserDetailsDto?> Handle(GetApiUser query, CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().Where(u => u.Id == query.Id).ToApiUser().FirstOrDefaultAsync(cancellationToken);
}

internal static class ApiUsers
{
    public static IQueryable<UserDetailsDto> ToApiUser(this IQueryable<ApplicationUser> users) => users.Select(u => new UserDetailsDto
    {
        Id = u.Id,
        UserName = u.UserName,
        FirstName = u.FirstName,
        LastName = u.LastName,
        FullName = u.FullName, // as legacy's API: the stored column (staff screens show PersonNames)
        Email = u.Email,
        IsActive = u.IsActive,
        EmailConfirmed = u.EmailConfirmed,
        PhoneNumber = u.PhoneNumber,
        ImageUrl = u.ImageUrl,
        DateOfBirth = u.DateOfBirth,
        Gender = u.Gender,
        Nationality = u.Nationality,
        Address = u.Address,
        City = u.City,
        ZipPostalCode = u.ZipPostalCode,
        State = u.State,
        PassportNumber = u.PassportNumber,
        PassportIssueDate = u.PassportIssueDate,
        PassportExpiryDate = u.PassportExpiryDate,
        PassportIssuingCountry = u.PassportIssuingCountry,
        VisaIssueDate = u.VisaIssueDate,
        VisaExpiryDate = u.VisaExpiryDate,
        CloudbedsGuestId = u.CloudbedsGuestId,
    });
}

// Bookings -------------------------------------------------------------------------------------------------------

internal sealed class SearchApiBookingsHandler(IAppDbContext db) : IQueryHandler<SearchApiBookings, PaginationResponse<BookingListDto>>
{
    public async ValueTask<PaginationResponse<BookingListDto>> Handle(SearchApiBookings query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var empty = new PaginationResponse<BookingListDto>([], 0, request.PageNumber, request.PageSize);

        // A Cloudbeds guest id narrows to that guest's bookings (none when no guest has it).
        string? guestId = null;
        if (!string.IsNullOrWhiteSpace(request.CloudbedsGuestId))
        {
            guestId = await db.Users.AsNoTracking().Where(u => u.CloudbedsGuestId == request.CloudbedsGuestId).Select(u => u.Id).FirstOrDefaultAsync(cancellationToken);
            if (guestId is null)
                return empty;
        }

        // Tour bookings only, as legacy's search (hostel bookings are retired).
        var bookings = db.Bookings.AsNoTracking()
            .Where(b => request.TourId != null ? b.Items.Any(i => i.TourId == request.TourId) : b.Items.Any(i => i.TourId != null));
        if (!string.IsNullOrWhiteSpace(request.Keyword))
            bookings = bookings.Where(Keyword(request.Keyword));
        if (request.Description is not null)
            bookings = bookings.Where(b => b.Description == request.Description);
        if (request.BookingStartDate is { } bookedFrom && request.BookingEndDate is { } bookedTo)
            bookings = bookings.Where(b => b.BookingDate >= bookedFrom && b.BookingDate <= bookedTo);
        if (request.TourStartDate is { } departsFrom && request.TourEndDate is { } departsTo)
            bookings = bookings.Where(b => b.Items.Any(i => i.StartDate >= departsFrom && i.StartDate <= departsTo));
        if (guestId is not null)
            bookings = bookings.Where(b => b.GuestId == guestId);

        var count = await bookings.CountAsync(cancellationToken);
        var page = await Sorted(bookings, request.OrderBy)
            .Skip((Math.Max(request.PageNumber, 1) - 1) * request.PageSize).Take(request.PageSize)
            .Select(b => new BookingListDto
            {
                Id = b.Id,
                Description = b.Description,
                TotalAmount = b.TotalAmount,
                CurrencyCode = b.CurrencyCode,
                IsPaid = b.IsPaid,
                BookingDate = b.BookingDate,
                GuestId = b.GuestId,
                GuestName = b.GuestName,
                GuestEmail = b.GuestEmail,
                InvoiceId = b.InvoiceId,
                ConcurrencyVersion = b.ConcurrencyVersion,
                AdditionalNotes = b.AdditionalNotes,
                CreatedBy = b.CreatedBy,
                WaiverSigned = b.WaiverSigned,
                BookingSource = b.BookingSource,
                ConfirmationEmailSent = b.ConfirmationEmailSent,
                Refunded = b.Refunded,
                AmountOutstanding = b.AmountOutstanding,
                Cancelled = b.Cancelled,
                PartialRefund = b.PartialRefund,
                ContainsPackage = b.BookingPackages.Any(),
                PackIncentiveId = b.PackIncentiveId,
            })
            .ToListAsync(cancellationToken);

        // The lead guests' Cloudbeds ids.
        var guestIds = page.Select(b => b.GuestId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var cloudbeds = await db.Users.AsNoTracking().Where(u => guestIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.CloudbedsGuestId, cancellationToken);
        foreach (var booking in page)
            booking.CloudbedsGuestId = booking.GuestId is { } id ? cloudbeds.GetValueOrDefault(id) : null;

        return new PaginationResponse<BookingListDto>(page, count, request.PageNumber, request.PageSize);
    }

    /// <summary>Legacy's keyword: the description or guest's name, else the invoice number, the booking's day or its total.</summary>
    private static System.Linq.Expressions.Expression<Func<Domain.Catalog.Bookings.Booking, bool>> Keyword(string keyword)
    {
        var like = $"%{keyword}%";
        var isNumber = int.TryParse(keyword, out _);
        var isDate = DateTime.TryParse(keyword, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date);
        var isAmount = decimal.TryParse(keyword, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amount);
        var day = date.Date;
        var nextDay = day.AddDays(1);
        return b => EF.Functions.Like(b.Description, like)
                    || EF.Functions.Like(b.GuestName!, like)
                    || (isNumber && EF.Functions.Like(b.InvoiceId.ToString(), like))
                    || (isDate && b.BookingDate >= day && b.BookingDate < nextDay)
                    || (isAmount && b.TotalAmount == amount);
    }

    /// <summary>"{Column} Ascending|Descending" (the first entry), else the newest bookings first.</summary>
    private static IQueryable<Domain.Catalog.Bookings.Booking> Sorted(IQueryable<Domain.Catalog.Bookings.Booking> bookings, string[]? orderBy)
    {
        var parts = orderBy is { Length: > 0 } ? orderBy[0].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
        if (parts.Length == 0)
            return bookings.OrderByDescending(b => b.BookingDate);
        var descending = parts.Length > 1 && parts[1].StartsWith("desc", StringComparison.OrdinalIgnoreCase);
        return parts[0].ToLowerInvariant() switch
        {
            "totalamount" => descending ? bookings.OrderByDescending(b => b.TotalAmount) : bookings.OrderBy(b => b.TotalAmount),
            "invoiceid" => descending ? bookings.OrderByDescending(b => b.InvoiceId) : bookings.OrderBy(b => b.InvoiceId),
            "bookingdate" => descending ? bookings.OrderByDescending(b => b.BookingDate) : bookings.OrderBy(b => b.BookingDate),
            _ => bookings.OrderByDescending(b => b.BookingDate),
        };
    }
}

internal sealed class GetApiBookingHandler(IAppDbContext db) : IQueryHandler<GetApiBooking, BookingDetailsDto?>
{
    public async ValueTask<BookingDetailsDto?> Handle(GetApiBooking query, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.AsNoTracking().AsSplitQuery()
            .Include(b => b.Payments)
            .Include(b => b.BookingPackages)
            .Include(b => b.Items).ThenInclude(i => i.Guests)
            .Include(b => b.Items).ThenInclude(i => i.TourDate).ThenInclude(d => d!.TourPrice)
            .Include(b => b.Items).ThenInclude(i => i.Tour).ThenInclude(t => t!.TourCategory)
            .Include(b => b.Items).ThenInclude(i => i.TourCategory)
            .FirstOrDefaultAsync(b => b.Id == query.Id, cancellationToken);
        if (booking is null)
            return null;

        // Guests on the tours, with their names and Cloudbeds ids.
        var items = booking.Items.Where(i => i.DeletedOn == null).OrderBy(i => i.StartDate).ToList();
        var guestIds = items.SelectMany(i => i.Guests ?? []).Where(g => g.DeletedOn == null).Select(g => g.GuestId.ToString())
            .Append(booking.GuestId ?? "").Where(id => id != "").Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => guestIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.FirstName, u.LastName, u.Email, u.CloudbedsGuestId, u.DateOfBirth, u.Nationality, u.Gender })
            .ToDictionaryAsync(u => u.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var payments = booking.Payments.Where(p => p.DeletedOn == null).OrderBy(p => p.CreatedOn).ToList();
        return new BookingDetailsDto
        {
            Id = booking.Id,
            Description = booking.Description,
            TotalAmount = booking.TotalAmount,
            CurrencyCode = booking.CurrencyCode,
            ItemQuantity = booking.ItemQuantity,
            IsPaid = booking.IsPaid,
            BookingDate = booking.BookingDate,
            GuestId = booking.GuestId,
            GuestName = booking.GuestName,
            GuestEmail = booking.GuestEmail,
            CloudbedsGuestId = booking.GuestId is { } lead && users.TryGetValue(lead, out var leadUser) ? leadUser.CloudbedsGuestId : null,
            InvoiceId = booking.InvoiceId,
            ConcurrencyVersion = booking.ConcurrencyVersion,
            StripeSessionId = booking.StripeSessionId,
            AdditionalNotes = booking.AdditionalNotes,
            WaiverSigned = booking.WaiverSigned,
            BookingSource = booking.BookingSource,
            ConfirmationEmailSent = booking.ConfirmationEmailSent,
            Refunded = booking.Refunded,
            AmountOutstanding = booking.AmountOutstanding,
            CreatedBy = booking.CreatedBy,
            Items =
            [
                .. items.Select(i => new BookingItemDetailsDto
                {
                    Id = i.Id,
                    BookingId = i.BookingId,
                    StartDate = i.StartDate,
                    EndDate = i.EndDate,
                    CreatedOn = i.CreatedOn,
                    Amount = i.Amount,
                    RoomQuantity = i.RoomQuantity,
                    PropertyId = i.PropertyId,
                    TourId = i.TourId,
                    TourName = i.Tour?.Name,
                    TourDateId = i.TourDateId,
                    TourPriceId = i.TourDate?.TourPriceId,
                    CloudbedsReservationId = i.CloudbedsReservationId,
                    CloudbedsPropertyId = i.CloudbedsPropertyId,
                    ConcurrencyVersion = i.ConcurrencyVersion,
                    PickupLocation = i.PickupLocation,
                    WaiverSigned = i.WaiverSigned,
                    TourCategoryId = i.TourCategoryId,
                    BookingPackageId = i.BookingPackageId,
                    NoShow = i.NoShow,
                    Cancelled = i.Cancelled,
                    CheckedIn = i.CheckedIn,
                    Refunded = i.Refunded,
                    PartialRefund = i.PartialRefund,
                    Tour = i.Tour is null ? null : ApiTours.ToDto(i.Tour),
                    TourDate = i.TourDate is null ? null : ApiTours.ToDto(i.TourDate),
                    TourPrice = i.TourDate?.TourPrice is null ? null : ApiTours.ToDto(i.TourDate.TourPrice),
                    TourCategory = i.TourCategory is null ? null : ApiTours.ToDto(i.TourCategory),
                    Guests =
                    [
                        .. (i.Guests ?? []).Where(g => g.DeletedOn == null).Select(g =>
                        {
                            var user = users.GetValueOrDefault(g.GuestId.ToString());
                            var name = user is null ? null : !string.IsNullOrWhiteSpace(user.FullName) ? user.FullName : $"{user.FirstName} {user.LastName}".Trim();
                            return new BookingItemGuestDto
                            {
                                Id = g.Id,
                                BookingItemId = g.BookingItemId,
                                GuestId = g.GuestId,
                                FullName = string.IsNullOrWhiteSpace(name) ? null : name,
                                Email = user?.Email,
                                CloudbedsGuestId = user?.CloudbedsGuestId,
                                DateOfBirth = user?.DateOfBirth,
                                Nationality = user?.Nationality,
                                Gender = user?.Gender,
                                NoShow = g.NoShow,
                                Cancelled = g.Cancelled,
                                CheckedIn = g.CheckedIn,
                            };
                        }),
                    ],
                }),
            ],
            BookingPackages =
            [
                .. booking.BookingPackages.Where(p => p.DeletedOn == null).Select(p => new BookingPackageDto
                {
                    Id = p.Id,
                    BookingId = p.BookingId,
                    PackageId = p.PackageId,
                    PackageName = p.PackageName,
                    PackagePrice = p.PackagePrice,
                    TierTitle = p.TierTitle,
                    CommissionAmount = p.CommissionAmount,
                    CommissionIgnoresMinimumPax = p.CommissionIgnoresMinimumPax,
                }),
            ],
            Payments =
            [
                .. payments.Select(p => new BookingPaymentDto
                {
                    Id = p.Id,
                    BookingId = p.BookingId,
                    BookingInvoiceId = booking.InvoiceId,
                    Amount = p.Amount,
                    Refunded = p.Refunded,
                    StripeSessionId = p.StripeSessionId,
                    CustomerName = p.CustomerName,
                    CustomerEmail = p.CustomerEmail,
                    Currency = p.Currency,
                    PaymentDate = p.PaymentDate,
                    PaymentMethod = p.PaymentMethod,
                }),
            ],
            Cancelled = booking.Cancelled,
            PaymentMethod = payments.FirstOrDefault()?.PaymentMethod ?? BookingPaymentMethod.Stripe,
            PackIncentiveId = booking.PackIncentiveId,
            PackIncentiveTierId = booking.PackIncentiveTierId,
        };
    }
}

internal static class ApiTours
{
    public static TourDto ToDto(Domain.Catalog.Tours.Tour t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Description = t.Description,
        ShortDescription = t.ShortDescription,
        Price = t.Price,
        PriceLabel = t.PriceLabel,
        ImagePath = t.ImagePath,
        ThumbnailImagePath = t.ThumbnailImagePath,
        MaxCapacity = t.MaxCapacity,
        MinCapacity = t.MinCapacity,
        DayDuration = t.DayDuration,
        NightDuration = t.NightDuration,
        HourDuration = t.HourDuration,
        Address = t.Address,
        TelephoneNumber = t.TelephoneNumber,
        WhatsIncluded = t.WhatsIncluded,
        WhatsNotIncluded = t.WhatsNotIncluded,
        AdditionalInformation = t.AdditionalInformation,
        MetaKeywords = t.MetaKeywords,
        MetaDescription = t.MetaDescription,
        BookingConfirmationEmailDetails = t.BookingConfirmationEmailDetails,
        TermsAndConditions = t.TermsAndConditions,
        CancellationPolicy = t.CancellationPolicy,
        WaiverRequired = t.WaiverRequired,
        PublishToSite = t.PublishToSite,
        SupplierEmailText = t.SupplierEmailText,
        ShowBookingQRCode = t.ShowBookingQRCode,
        ExcludeFromStaffSales = t.ExcludeFromStaffSales,
        UrlSlug = t.UrlSlug,
        CustomSeoScripts = t.CustomSeoScripts,
        SeoPageTitle = t.SeoPageTitle,
        LastModifiedOn = t.LastModifiedOn,
        CreatedOn = t.CreatedOn,
        SupplierId = t.SupplierId,
        TourCategoryId = t.TourCategoryId,
        TourCategory = t.TourCategory is null ? null : ToDto(t.TourCategory),
    };

    public static TourCategoryDto ToDto(Domain.Catalog.Tours.TourCategory c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Description = c.Description,
        IconClass = c.IconClass,
        ShortDescription = c.ShortDescription,
        ImagePath = c.ImagePath,
        ThumbnailImagePath = c.ThumbnailImagePath,
        MetaKeywords = c.MetaKeywords,
        MetaDescription = c.MetaDescription,
        TopLevelCategory = c.TopLevelCategory,
        ParentTourCategoryId = c.ParentTourCategoryId,
    };

    public static TourDateDto ToDto(Domain.Catalog.Tours.TourDate d) => new()
    {
        Id = d.Id,
        StartDate = d.StartDate,
        TourId = d.TourId,
        EndDate = d.EndDate,
        AvailableSpaces = d.AvailableSpaces,
        TourPriceId = d.TourPriceId,
        ConcurrencyVersion = d.ConcurrencyVersion,
        TourPrice = d.TourPrice is null ? null : ToDto(d.TourPrice),
    };

    public static TourPriceDto ToDto(Domain.Catalog.Tours.TourPrice p) => new()
    {
        Id = p.Id,
        Price = p.Price,
        TourId = p.TourId,
        Title = p.Title,
        Description = p.Description,
        MonthFrom = p.MonthFrom,
        MonthTo = p.MonthTo,
        DayDuration = p.DayDuration,
        NightDuration = p.NightDuration,
        HourDuration = p.HourDuration,
        ComissionAmount = p.ComissionAmount,
        CommissionMinimumPax = p.CommissionMinimumPax,
        CommissionMinimumPaxScope = p.CommissionMinimumPaxScope,
        PublishToWebsite = p.PublishToWebsite,
        InHouseOnly = p.InHouseOnly,
    };
}
