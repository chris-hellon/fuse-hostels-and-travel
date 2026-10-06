using Fuse.Domain.Identity;
using Fuse.Application.Features.Identity;
using System.Globalization;
using Microsoft.Extensions.Options;
using Fuse.Application.Common.Files;
using Fuse.Application.Common.Time;
using Fuse.Application.Features.Tours;
using Fuse.Contracts.Bookings;
using Fuse.Contracts.Dashboard;
using Fuse.Contracts.Tours;
using Fuse.Domain.Catalog.Bookings;

namespace Fuse.Application.Features.Bookings;

internal sealed class SearchBookingsHandler(IAppDbContext db, SupplierScope scope) : IQueryHandler<SearchBookings, PagedResult<BookingRow>>
{
    public async ValueTask<PagedResult<BookingRow>> Handle(SearchBookings query, CancellationToken cancellationToken)
    {
        var tours = await scope.TourIdsAsync(cancellationToken);
        if (tours is { Count: 0 })
            return PagedResult<BookingRow>.Empty(query.PageSize);

        return await BookingFilters.Apply(db.Bookings.AsNoTracking(), query, tours)
            .Select(b => new BookingRow
            {
                Id = b.Id,
                Reference = b.InvoiceId,
                Description = b.Description,
                GuestId = b.GuestId,
                GuestName = b.GuestName,
                GuestHasEmail = b.GuestEmail != null && b.GuestEmail != "" && !b.GuestEmail.EndsWith(PlaceholderEmails.Suffix),
                BookingDate = b.BookingDate,
                TotalAmount = b.TotalAmount,
                Outstanding = b.AmountOutstanding > 0 ? b.AmountOutstanding.Value : b.IsPaid ? 0m : b.TotalAmount,
                Status = b.Cancelled == true ? BookingState.Cancelled
                    : b.Refunded == true ? BookingState.Refunded
                    : b.PartialRefund == true ? BookingState.PartialRefund
                    : b.AmountOutstanding > 0 ? BookingState.PartiallyPaid
                    : b.IsPaid ? BookingState.Paid
                    : BookingState.Unpaid,
                IsPaid = b.IsPaid,
                Cancelled = b.Cancelled == true,
                Refunded = b.Refunded == true,
                ContainsPackage = b.Items.Any(i => i.BookingPackageId != null),
                HasPackIncentive = b.PackIncentiveId != null,
            })
            .OrderBy(query.OrderBy, b => b.BookingDate, descending: true)
            .ToPagedResultAsync(query, cancellationToken);
    }
}

internal static class BookingFilters
{
    /// <summary>Tour bookings matching the table's search and Advanced Search filters.</summary>
    public static IQueryable<Booking> Apply(IQueryable<Booking> bookings, SearchBookings query, IReadOnlyList<DefaultIdType>? tours)
    {
        // Property bookings are retired: the table only ever lists bookings with tours.
        bookings = bookings.Where(b => b.Items.Any(i => i.TourId != null));
        if (tours is not null)
            bookings = bookings.Where(b => b.Items.Any(i => i.TourId != null && tours.Contains(i.TourId.Value)));
        if (query.TourId is { } tourId)
            bookings = bookings.Where(b => b.Items.Any(i => i.TourId == tourId));
        if (query.BookedFrom is { } bookedFrom)
            bookings = bookings.Where(b => b.BookingDate >= bookedFrom.Date);
        if (query.BookedTo is { } bookedTo)
        {
            var end = bookedTo.Date.AddDays(1);
            bookings = bookings.Where(b => b.BookingDate < end);
        }
        if (query.DepartsFrom is not null || query.DepartsTo is not null)
        {
            var from = query.DepartsFrom?.Date ?? DateTime.MinValue;
            var to = query.DepartsTo?.Date.AddDays(1) ?? DateTime.MaxValue;
            bookings = bookings.Where(b => b.Items.Any(i => i.StartDate >= from && i.StartDate < to));
        }
        if (!string.IsNullOrWhiteSpace(query.Source))
            bookings = bookings.Where(b => b.BookingSource == query.Source);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            // As legacy: description or guest, a reference, a booking date or a total. A reference is
            // matched exactly (legacy's partial match scanned every booking).
            var keyword = query.Keyword.Trim();
            var isReference = int.TryParse(keyword, out var reference);
            var isDate = DateTime.TryParse(keyword, CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.None, out var date);
            var dayAfter = date.Date.AddDays(1);
            var isAmount = decimal.TryParse(keyword, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount);
            bookings = bookings.Where(b => b.Description.Contains(keyword)
                                           || (b.GuestName != null && b.GuestName.Contains(keyword))
                                           || (isReference && b.InvoiceId == reference)
                                           || (isDate && b.BookingDate >= date.Date && b.BookingDate < dayAfter)
                                           || (isAmount && b.TotalAmount == amount));
        }

        return bookings;
    }
}

internal sealed class ExportBookingsHandler(IAppDbContext db, ITenantContext tenant, SupplierScope scope, ISpreadsheetWriter spreadsheets, IBusinessClock clock)
    : IQueryHandler<ExportBookings, FileDownload>
{
    public async ValueTask<FileDownload> Handle(ExportBookings query, CancellationToken cancellationToken)
    {
        var export = ExportWindow.Normalize(query, clock.Today).Export;
        var rows = await BookingExports.QueryAsync(db, tenant, scope, new BookingExports.Filter
        {
            BookingStart = export.BookedFrom,
            BookingEnd = export.BookedTo?.AddDays(1).AddSeconds(-1),
            TourStart = export.DepartsFrom,
            TourEnd = export.DepartsTo?.AddDays(1).AddSeconds(-1),
            TourId = export.TourId,
            Keyword = export.Keyword,
            HideRefunded = false,
        }, cancellationToken);
        return spreadsheets.Write($"Bookings {clock.Today:yyyy-MM-dd}.xlsx", rows, BookingExports.Columns);
    }
}

internal sealed class ListBookingSourcesHandler(IAppDbContext db) : IQueryHandler<ListBookingSources, IReadOnlyList<string>>
{
    public static readonly string[] Fixed = ["Website", "Online", "Booking App"];

    public async ValueTask<IReadOnlyList<string>> Handle(ListBookingSources query, CancellationToken cancellationToken) =>
        await ListAsync(db, cancellationToken);

    public static async Task<IReadOnlyList<string>> ListAsync(IAppDbContext db, CancellationToken cancellationToken)
    {
        var properties = await db.Properties.AsNoTracking().OrderBy(p => p.Name).Select(p => p.Name).ToListAsync(cancellationToken);
        return [.. Fixed, .. properties.Where(p => !Fixed.Contains(p))];
    }
}

internal sealed class ListBookingToursHandler(IAppDbContext db, SupplierScope scope) : IQueryHandler<ListBookingTours, IReadOnlyList<LookupItem>>
{
    public async ValueTask<IReadOnlyList<LookupItem>> Handle(ListBookingTours query, CancellationToken cancellationToken)
    {
        var tours = await scope.TourIdsAsync(cancellationToken);
        var list = db.Tours.AsNoTracking();
        if (tours is not null)
            list = list.Where(t => tours.Contains(t.Id));
        var rows = await list.OrderBy(t => t.Name).Select(t => new { t.Id, t.Name }).ToListAsync(cancellationToken);
        return [.. rows.Select(t => new LookupItem(t.Id.ToString(), t.Name))];
    }
}

internal sealed class GetBookingAbilitiesHandler(ICurrentUser user, IOptions<BookingOptions> options) : IQueryHandler<GetBookingAbilities, BookingAbilities>
{
    public ValueTask<BookingAbilities> Handle(GetBookingAbilities query, CancellationToken cancellationToken) =>
        ValueTask.FromResult(BookingPolicy.For(user, options.Value));
}

internal static class BookingPolicy
{
    public static BookingAbilities For(ICurrentUser user, BookingOptions options) => new()
    {
        CanRefund = user.Principal.HasPermission(FuseAction.Delete, FuseResource.TourBookings),
        CanFlagAsPaid = user.UserId is { } id && options.FlagAsPaidUserIds.Contains(id.ToString(), StringComparer.OrdinalIgnoreCase),
        CanChangeSalesPerson = user.Principal.HasPermission(FuseAction.Update, FuseResource.Users),
        IsAdmin = user.IsInRole(FuseRoles.Admin),
    };
}

internal sealed class GetBookingOverviewHandler(IAppDbContext db, SupplierScope scope) : IQueryHandler<GetBookingOverview, BookingOverview>
{
    public async ValueTask<BookingOverview> Handle(GetBookingOverview query, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.AsNoTracking().AsSplitQuery()
            .Include(b => b.Items).ThenInclude(i => i.Guests)
            .Include(b => b.Items).ThenInclude(i => i.Tour)
            .Include(b => b.Items).ThenInclude(i => i.BookingPackage)
            .Include(b => b.Payments)
            .FirstOrDefaultAsync(b => b.Id == query.Id, cancellationToken)
            ?? throw new NotFoundException("Booking not found.");
        await BookingAccess.EnsureVisibleAsync(booking, scope, cancellationToken);

        var items = booking.Items.Where(i => i.TourId != null).OrderBy(i => i.StartDate).ToList();
        var userIds = items.SelectMany(i => i.Guests ?? []).Select(g => g.GuestId.ToString())
            .Append(booking.GuestId ?? "").Append(booking.CreatedBy.ToString())
            .Where(id => id != "").Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.FullName, u.DateOfBirth, u.Nationality, u.Gender, u.Email })
            .ToDictionaryAsync(u => u.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        BookingGuestView Guest(string id, bool? cancelled, bool? noShow, bool? checkedIn, BookingItem item, string? fallbackName)
        {
            users.TryGetValue(id, out var u);
            return new BookingGuestView
            {
                GuestId = id,
                Name = u is null ? fallbackName : PersonNames.Display(u.FirstName, u.LastName, u.FullName),
                DateOfBirth = u?.DateOfBirth,
                Nationality = u?.Nationality,
                Gender = u?.Gender,
                Email = PlaceholderEmails.Visible(u?.Email),
                Status = BookingStatuses.Of(cancelled ?? item.Cancelled, item.Refunded, item.PartialRefund, noShow, checkedIn, booking),
            };
        }

        // A guest who booked themselves on the website "created" the booking (legacy "Direct from Website").
        var direct = booking.GuestId is not null && booking.CreatedBy.ToString().Equals(booking.GuestId, StringComparison.OrdinalIgnoreCase);
        users.TryGetValue(booking.CreatedBy.ToString(), out var staff);

        return new BookingOverview
        {
            Id = booking.Id,
            AdditionalNotes = booking.AdditionalNotes,
            AmountOutstanding = booking.AmountOutstanding,
            BookedBy = direct || staff is null ? null : PersonNames.Display(staff.FirstName, staff.LastName, staff.FullName),
            BookingSource = booking.BookingSource,
            GuestId = booking.GuestId,
            GuestName = booking.GuestId is { } guestId && users.TryGetValue(guestId, out var lead)
                ? PersonNames.Display(lead.FirstName, lead.LastName, lead.FullName)
                : booking.GuestName,
            GuestDetail = booking.GuestId is { } leadGuestId && users.TryGetValue(leadGuestId, out var leadGuest)
                ? string.Join(" - ", new[]
                {
                    leadGuest.DateOfBirth?.ToString("dd/MM/yyyy"),
                    Shared.Globalization.Genders.DisplayName(leadGuest.Gender),
                    Shared.Globalization.Countries.DisplayName(leadGuest.Nationality),
                    PlaceholderEmails.Visible(leadGuest.Email),
                }.Where(p => !string.IsNullOrWhiteSpace(p)))
                : null,
            Items =
            [
                .. items.Select(i => new BookingItemView
                {
                    Id = i.Id,
                    TourName = i.Tour?.Name ?? "",
                    PackageName = i.BookingPackage?.PackageName,
                    StartDate = i.StartDate,
                    EndDate = i.EndDate,
                    PickupLocation = i.PickupLocation,
                    Price = i.Amount,
                    GuestCount = BookingStatuses.GuestCount(i),
                    Status = BookingStatuses.Of(i.Cancelled, i.Refunded, i.PartialRefund, i.NoShow, i.CheckedIn, booking),
                    WaiverSigned = i.WaiverSigned == true,
                    Cancelled = i.Cancelled == true,
                    Refunded = i.Refunded == true,
                    Guests =
                    [
                        .. booking.GuestId is { } leadId ? [Guest(leadId, i.Cancelled, i.NoShow, i.CheckedIn, i, booking.GuestName)] : Array.Empty<BookingGuestView>(),
                        .. (i.Guests ?? []).Where(g => g.DeletedOn == null)
                            .Select(g => Guest(g.GuestId.ToString(), g.Cancelled, g.NoShow, g.CheckedIn, i, null)),
                    ],
                }),
            ],
            Payments =
            [
                .. booking.Payments.OrderBy(p => p.PaymentDate).Select(p => new BookingPaymentView
                {
                    Amount = p.Amount,
                    PaymentDate = p.PaymentDate,
                    CustomerName = p.CustomerName,
                    CustomerEmail = p.CustomerEmail,
                    Method = p.PaymentMethod ?? BookingPaymentMethod.Stripe,
                    Refunded = p.Refunded,
                }),
            ],
        };
    }
}

internal static class BookingAccess
{
    /// <summary>Suppliers only see bookings that include one of their tours.</summary>
    public static async Task EnsureVisibleAsync(Booking booking, SupplierScope scope, CancellationToken cancellationToken)
    {
        var tours = await scope.TourIdsAsync(cancellationToken);
        if (tours is not null && !booking.Items.Any(i => i.TourId is { } id && tours.Contains(id)))
            throw new NotFoundException("Booking not found.");
    }
}
