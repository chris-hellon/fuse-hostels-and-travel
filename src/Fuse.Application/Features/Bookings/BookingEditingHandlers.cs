using Fuse.Application.Features.Identity;
using Microsoft.Extensions.Options;
using Fuse.Application.Common.Time;
using Fuse.Application.Features.Tours;
using Fuse.Contracts.Bookings;
using Fuse.Contracts.Tours;
using Fuse.Domain.Catalog.Bookings;
using Fuse.Domain.Catalog.Commission;
using Fuse.Domain.Catalog.Packages;

namespace Fuse.Application.Features.Bookings;

internal sealed class GetBookingCatalogHandler(IAppDbContext db) : IQueryHandler<GetBookingCatalog, BookingCatalog>
{
    public async ValueTask<BookingCatalog> Handle(GetBookingCatalog query, CancellationToken cancellationToken)
    {
        var tours = await db.Tours.AsNoTracking().OrderBy(t => t.Name)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.TourCategoryId,
                t.WaiverRequired,
                Pickups = t.TourPickupLocations!.Select(p => p.Property.Name).ToList(),
            })
            .ToListAsync(cancellationToken);
        var categories = await db.TourCategories.AsNoTracking().OrderBy(c => c.Name).Select(c => new { c.Id, c.Name }).ToListAsync(cancellationToken);
        var packs = await db.PackIncentives.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.MinimumProductCount)
            .Include(p => p.Tours).Include(p => p.Tiers).ToListAsync(cancellationToken);
        var packages = await BookingPackages.LoadAsync(db, null, cancellationToken);

        return new BookingCatalog
        {
            Tours =
            [
                .. tours.Select(t => new BookableTour
                {
                    Id = t.Id,
                    Name = t.Name,
                    CategoryId = t.TourCategoryId,
                    WaiverRequired = t.WaiverRequired == true,
                    Pickups = [.. t.Pickups.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().Order()],
                }),
            ],
            Categories = [.. categories.Select(c => new LookupItem(c.Id.ToString(), c.Name))],
            Sources = await ListBookingSourcesHandler.ListAsync(db, cancellationToken),
            Packs = [.. packs.Select(BookingPacks.ToOption)],
            Packages = [.. packages.Select(BookingPackages.ToOption)],
        };
    }
}

internal static class BookingPacks
{
    public static PackOption ToOption(PackIncentive pack) => new()
    {
        Id = pack.Id,
        Name = pack.Name,
        MinimumTours = pack.MinimumProductCount,
        TourIds = [.. (pack.Tours ?? []).Select(t => t.TourId)],
        Tiers = [.. (pack.Tiers ?? []).OrderBy(t => t.SortOrder).Select(t => new PackTierOption(t.Id, t.Title, t.Price, t.SortOrder))],
    };
}

internal static class BookingPackages
{
    /// <summary>Packages with their tiers and priced tours (all, or just <paramref name="ids"/>).</summary>
    public static Task<List<Package>> LoadAsync(IAppDbContext db, IReadOnlyCollection<DefaultIdType>? ids, CancellationToken cancellationToken)
    {
        var packages = db.Packages.AsNoTracking().AsSplitQuery()
            .Include(p => p.PackageTiers)
            .Include(p => p.PackageItems!).ThenInclude(i => i.PackageItemPrices)
            .Include(p => p.PackageItems!).ThenInclude(i => i.Tour)
            .Where(p => p.PackageItems!.Any());
        if (ids is not null)
            packages = packages.Where(p => ids.Contains(p.Id));
        return packages.OrderBy(p => p.Name).ToListAsync(cancellationToken);
    }

    public static PackageOption ToOption(Package package) => new()
    {
        Id = package.Id,
        Name = package.Name,
        ShortDescription = package.ShortDescription,
        Tiers = [.. (package.PackageTiers ?? []).OrderBy(t => t.SortOrder).Select(t => new PackageTierOption(t.Id, t.Title, t.SortOrder))],
        Items =
        [
            .. (package.PackageItems ?? []).OrderBy(i => i.SortOrder).Select(i => new PackageItemOption
            {
                Id = i.Id,
                TourId = i.TourId,
                TourName = i.Tour?.Name ?? "",
                Quantity = Math.Max(1, i.Quantity),
                TourPriceId = i.TourPriceId,
                Prices = (i.PackageItemPrices ?? []).GroupBy(p => p.PackageTierId).ToDictionary(g => g.Key, g => g.First().Price),
            }),
        ],
    };
}

internal sealed class ListTourDeparturesHandler(IAppDbContext db, IBusinessClock clock, ICurrentUser user) : IQueryHandler<ListTourDepartures, IReadOnlyList<DepartureOption>>
{
    public async ValueTask<IReadOnlyList<DepartureOption>> Handle(ListTourDepartures query, CancellationToken cancellationToken)
    {
        var from = user.IsInRole(FuseRoles.Admin) ? clock.Today.AddMonths(-1) : clock.Now;
        return await db.TourDates.AsNoTracking()
            .Where(d => d.TourId == query.TourId && d.StartDate >= from && d.AvailableSpaces > 0)
            .OrderBy(d => d.StartDate).ThenBy(d => d.TourPrice.Price)
            .Select(d => new DepartureOption
            {
                Id = d.Id,
                PriceId = d.TourPriceId,
                PriceTitle = d.TourPrice.Title,
                Price = d.PriceOverride ?? d.TourPrice.Price,
                StartDate = d.StartDate,
                EndDate = d.EndDate,
                AvailableSpaces = d.AvailableSpaces,
            })
            .Take(2000)
            .ToListAsync(cancellationToken);
    }
}

internal sealed class GetTourWaiverHandler(IAppDbContext db) : IQueryHandler<GetTourWaiver, string>
{
    public async ValueTask<string> Handle(GetTourWaiver query, CancellationToken cancellationToken) =>
        await db.Tours.AsNoTracking().Where(t => t.Id == query.TourId).Select(t => t.TermsAndConditions).FirstOrDefaultAsync(cancellationToken) ?? "";
}

internal sealed class FindBookingStaffHandler(IAppDbContext db) : IQueryHandler<FindBookingStaff, IReadOnlyList<LookupItem>>
{
    public async ValueTask<IReadOnlyList<LookupItem>> Handle(FindBookingStaff query, CancellationToken cancellationToken)
    {
        var search = query.Search?.Trim();
        if (string.IsNullOrEmpty(search) || search.Length < 2)
            return [];

        var roleIds = db.Roles.Where(r => FuseRoles.StaffRoles.Contains(r.Name!)).Select(r => r.Id);
        var staff = await db.Users.AsNoTracking()
            .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && roleIds.Contains(ur.RoleId)))
            .Where(u => u.FirstName!.StartsWith(search) || u.LastName!.StartsWith(search) || u.Email!.StartsWith(search) || u.FullName!.StartsWith(search))
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .Take(20)
            .ToListAsync(cancellationToken);
        return [.. staff.Select(s => new LookupItem(s.Id, $"{s.FirstName} {s.LastName}".Trim() + (string.IsNullOrEmpty(s.Email) ? "" : $" ({s.Email})")))];
    }
}

internal sealed class GetBookingEditorHandler(IAppDbContext db, SupplierScope scope) : IQueryHandler<GetBookingEditor, BookingEditor>
{
    public async ValueTask<BookingEditor> Handle(GetBookingEditor query, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.AsNoTracking().AsSplitQuery()
            .Include(b => b.Items).ThenInclude(i => i.Guests)
            .Include(b => b.Items).ThenInclude(i => i.Tour)
            .Include(b => b.Items).ThenInclude(i => i.TourDate).ThenInclude(d => d!.TourPrice)
            .Include(b => b.BookingPackages)
            .Include(b => b.Payments)
            .FirstOrDefaultAsync(b => b.Id == query.Id, cancellationToken)
            ?? throw new NotFoundException("Booking not found.");
        await BookingAccess.EnsureVisibleAsync(booking, scope, cancellationToken);

        var savedPack = booking.PackIncentiveId is { } packId
            ? await db.PackIncentives.AsNoTracking().Include(p => p.Tours).Include(p => p.Tiers).FirstOrDefaultAsync(p => p.Id == packId, cancellationToken)
            : null;
        var savedPackTours = savedPack?.Tours?.Select(t => t.TourId).ToList() ?? [];
        var packageIds = booking.BookingPackages.Select(p => p.PackageId).Distinct().ToList();
        var packageTiers = await db.Packages.AsNoTracking().Where(p => packageIds.Contains(p.Id))
            .SelectMany(p => p.PackageTiers!.Select(t => new { t.PackageId, t.Id, t.Title }))
            .ToListAsync(cancellationToken);

        var userIds = new[] { booking.GuestId, booking.CreatedBy.ToString() }.Where(id => id is not null).ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.FullName ?? (u.FirstName + " " + u.LastName) })
            .ToDictionaryAsync(u => u.Id, u => u.Name, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var lines = booking.Items.Where(i => i.TourId != null).OrderBy(i => i.StartDate).ToList();
        var guestIds = lines.SelectMany(i => i.Guests ?? []).Where(g => g.DeletedOn == null).Select(g => g.GuestId.ToString()).Distinct().ToList();
        var guestNames = (await db.Users.AsNoTracking().Where(u => guestIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.FullName, u.DateOfBirth, u.Gender, u.Nationality, u.Email })
                .ToListAsync(cancellationToken))
            .ToDictionary(u => u.Id, u => GuestNames.Of(u.FullName ?? $"{u.FirstName} {u.LastName}".Trim(), u.DateOfBirth, u.Gender, u.Nationality, PlaceholderEmails.Visible(u.Email)),
                StringComparer.OrdinalIgnoreCase);
        var packagesInUse = lines.Where(i => i.BookingPackageId != null).Select(i => i.BookingPackageId!.Value).ToHashSet();
        var direct = booking.GuestId is not null && booking.CreatedBy.ToString().Equals(booking.GuestId, StringComparison.OrdinalIgnoreCase)
                     && booking.BookingSource != "Booking App";

        return new BookingEditor
        {
            Id = booking.Id,
            Reference = booking.InvoiceId,
            ConcurrencyVersion = booking.ConcurrencyVersion,
            BookingDate = booking.BookingDate,
            Description = booking.Description,
            GuestId = booking.GuestId,
            GuestName = booking.GuestId is { } g && names.TryGetValue(g, out var guestName) ? guestName : booking.GuestName,
            BookingSource = booking.BookingSource,
            AdditionalNotes = booking.AdditionalNotes,
            PaymentMethod = booking.Payments.OrderByDescending(p => p.CreatedOn).FirstOrDefault()?.PaymentMethod ?? BookingPaymentMethod.Stripe,
            BookedById = direct ? null : booking.CreatedBy.ToString(),
            BookedByName = direct ? null : names.GetValueOrDefault(booking.CreatedBy.ToString()),
            DirectFromWebsite = direct,
            PackIncentiveId = booking.PackIncentiveId,
            PackIncentiveTierId = booking.PackIncentiveTierId,
            SavedPackIncentiveId = booking.PackIncentiveId,
            SavedPackIncentiveTierId = booking.PackIncentiveTierId,
            SavedPack = savedPack is null ? null : BookingPacks.ToOption(savedPack),
            IsPaid = booking.IsPaid,
            Refunded = booking.Refunded == true,
            Cancelled = booking.Cancelled == true,
            SavedTotal = booking.TotalAmount,
            GuestNames = guestNames,
            AmountOutstanding = booking.AmountOutstanding,
            Packages =
            [
                .. booking.BookingPackages.Where(p => packagesInUse.Contains(p.Id)).Select(p => new BookingPackageLine
                {
                    Key = p.Id,
                    PackageId = p.PackageId,
                    PackageName = p.PackageName,
                    TierId = packageTiers.FirstOrDefault(t => t.PackageId == p.PackageId && t.Title == p.TierTitle)?.Id,
                    SavedTierId = packageTiers.FirstOrDefault(t => t.PackageId == p.PackageId && t.Title == p.TierTitle)?.Id,
                    TierTitle = p.TierTitle,
                    Price = p.PackagePrice,
                }),
            ],
            Lines =
            [
                .. lines.Select(i => new BookingLine
                {
                    Key = i.Id,
                    TourId = i.TourId!.Value,
                    TourName = i.Tour?.Name ?? "",
                    TourDateId = i.TourDateId ?? DefaultIdType.Empty,
                    PriceTitle = i.TourDate?.TourPrice?.Title ?? "",
                    StartDate = i.StartDate,
                    EndDate = i.EndDate,
                    DeparturePrice = i.TourDate is { } d ? d.PriceOverride ?? d.TourPrice?.Price ?? i.Amount : i.Amount,
                    Amount = i.Amount,
                    PickupLocation = i.PickupLocation,
                    WaiverSigned = i.WaiverSigned == true,
                    PackageKey = i.BookingPackageId,
                    GuestIds = [.. (i.Guests ?? []).Where(x => x.DeletedOn == null).Select(x => x.GuestId.ToString())],
                    Cancelled = i.Cancelled == true,
                    Refunded = i.Refunded == true,
                    SavedAmount = i.Amount,
                    SavedTourDateId = i.TourDateId,
                    SavedPackageKey = i.BookingPackageId,
                    SavedSpecialPrice = i.BookingPackageId != null || savedPackTours.Contains(i.TourId!.Value),
                }),
            ],
        };
    }
}

internal sealed class SaveBookingHandler(
    IAppDbContext db,
    SupplierScope scope,
    TourCapacity capacity,
    BookingRefunds refunds,
    ICurrentUser user,
    IOptions<BookingOptions> options,
    IBusinessClock clock) : ICommandHandler<SaveBooking, DefaultIdType>
{
    public async ValueTask<DefaultIdType> Handle(SaveBooking command, CancellationToken cancellationToken)
    {
        var input = command.Booking;
        var abilities = BookingPolicy.For(user, options.Value);
        var creating = input.Id is null;
        var userId = user.UserId ?? DefaultIdType.Empty;

        var guest = await db.Users.AsNoTracking().Where(u => u.Id == input.GuestId)
            .Select(u => new { u.Id, u.Email, Name = u.FullName ?? (u.FirstName + " " + u.LastName) })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException("The primary guest no longer exists. Choose another guest.");

        Booking booking;
        if (creating)
        {
            if (!user.Principal.HasPermission(FuseAction.Create, FuseResource.Bookings))
                throw new ForbiddenException("You can't create bookings.");
            booking = new Booking(input.Description, 0, "USD", 0, false, clock.Now, guest.Id, null, false, guest.Name, guest.Email,
                input.AdditionalNotes, input.BookingSource, null, null)
            {
                Items = [],
                Payments = [],
                OverrideCreatedBy = true,
                CreatedBy = userId,
            };
            db.Bookings.Add(booking);
        }
        else
        {
            booking = await db.Bookings.AsSplitQuery()
                .Include(b => b.Items).ThenInclude(i => i.Guests)
                .Include(b => b.Payments)
                .Include(b => b.BookingPackages)
                .FirstOrDefaultAsync(b => b.Id == input.Id, cancellationToken)
                ?? throw new NotFoundException("Booking not found.");
            await BookingAccess.EnsureVisibleAsync(booking, scope, cancellationToken);
            if (booking.Cancelled == true)
                throw new BusinessRuleException("Cancelled bookings can't be changed.");
            if (booking.ConcurrencyVersion != input.ConcurrencyVersion)
                throw new ConflictException("Someone else changed this booking while you were editing it. Close it and open it again.");
        }

        // As legacy (its booking dialog and BookingItemDialog): once paid or refunded, nobody adds or removes tours
        // or changes the packages or the pack; anyone may still move a tour to another departure or pick up (until
        // it's refunded), and administrators may also change a tour's tour and guests.
        var locked = !creating && (booking.IsPaid || booking.Refunded == true);
        var tourAndGuestsLocked = locked && !abilities.IsAdmin;
        var departureLocked = !creating && booking.Refunded == true;
        if (locked && (input.PackIncentiveId != booking.PackIncentiveId || (input.PackIncentiveId is not null && input.PackIncentiveTierId != booking.PackIncentiveTierId)))
            throw new BusinessRuleException("This booking is paid: its pack can't be changed.");
        var bookedBy = BookedBy(input, booking, creating, abilities);

        // Packages ------------------------------------------------------------------------------
        var packageIds = input.Packages.Select(p => p.PackageId).Distinct().ToList();
        var packageDefinitions = packageIds.Count == 0 ? [] : await BookingPackages.LoadAsync(db, packageIds, cancellationToken);
        var packageOptions = packageDefinitions.Select(BookingPackages.ToOption).ToList();
        var packagesByKey = new Dictionary<DefaultIdType, BookingPackage>();
        foreach (var line in input.Packages.Where(p => input.Lines.Any(l => l.PackageKey == p.Key)))
        {
            var saved = booking.BookingPackages.FirstOrDefault(p => p.Id == line.Key);
            if (saved is not null && !line.Edited)
            {
                // As legacy: a booked package keeps its snapshot (name, price, tier, commission) until it's edited
                // again in the package dialog, so staff commission history doesn't follow later catalog changes.
                line.SavedTierId = packageDefinitions.FirstOrDefault(p => p.Id == saved.PackageId)?.PackageTiers?.FirstOrDefault(t => t.Title == saved.TierTitle)?.Id;
                line.TierId = line.SavedTierId;
                packagesByKey[line.Key] = saved;
                continue;
            }

            if (locked)
                throw new BusinessRuleException("This booking is paid: its packages can't be changed.");
            var definition = packageDefinitions.FirstOrDefault(p => p.Id == line.PackageId)
                ?? throw new BusinessRuleException($"The package \"{line.PackageName}\" no longer exists.");
            var tier = definition.PackageTiers?.FirstOrDefault(t => t.Id == line.TierId) ?? definition.PackageTiers?.OrderBy(t => t.SortOrder).FirstOrDefault()
                ?? throw new BusinessRuleException($"The package \"{definition.Name}\" has no price tiers.");
            line.TierId = tier.Id;
            var price = definition.TotalPriceForTier(tier.Id);

            line.SavedTierId = saved is null ? null : definition.PackageTiers?.FirstOrDefault(t => t.Title == saved.TierTitle)?.Id;
            if (saved is not null)
            {
                saved.Update(definition.Name, price, tier.Title, definition.CommissionAmount, definition.CommissionIgnoresMinimumPax);
                packagesByKey[line.Key] = saved;
            }
            else
            {
                var added = new BookingPackage(definition.Id, definition.Name, price, tier.Title, definition.CommissionAmount,
                    definition.CommissionIgnoresMinimumPax, userId);
                booking.BookingPackages.Add(added);
                db.BookingPackages.Add(added);
                packagesByKey[line.Key] = added;
            }
        }

        // Tours ----------------------------------------------------------------------------------
        var savedItems = booking.Items.Where(i => i.TourId != null).ToDictionary(i => i.Id);
        var keys = input.Lines.Select(l => l.Key).ToHashSet();
        foreach (var removed in savedItems.Values.Where(i => !keys.Contains(i.Id)))
        {
            if (locked || removed.Cancelled == true || removed.Refunded == true)
                throw new BusinessRuleException($"The {removed.StartDate:d MMM} tour can't be removed from this booking. Cancel it from the booking's details instead.");
            await capacity.ReleaseAsync(removed.TourDateId, BookingStatuses.GuestCount(removed), cancellationToken);
            db.BookingItems.Remove(removed); // soft delete
        }

        var dateIds = input.Lines.Select(l => l.TourDateId).Distinct().ToList();
        var dates = await db.TourDates.AsNoTracking().Include(d => d.TourPrice).Where(d => dateIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, cancellationToken);
        var tourIds = dates.Values.Select(d => d.TourId).Distinct().ToList();
        var tours = await db.Tours.AsNoTracking().Where(t => tourIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.TourCategoryId }).ToDictionaryAsync(t => t.Id, cancellationToken);
        var guestIds = input.Lines.SelectMany(l => l.GuestIds).Distinct().ToList();
        var knownGuests = (await db.Users.Where(u => guestIds.Contains(u.Id)).Select(u => u.Id).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var itemsByLine = new Dictionary<BookingLine, BookingItem>();
        foreach (var line in input.Lines)
        {
            if (!dates.TryGetValue(line.TourDateId, out var date))
                throw new BusinessRuleException($"A departure of {line.TourName} no longer exists. Choose another date.");
            var tour = tours[date.TourId];
            line.TourId = date.TourId;
            line.TourName = tour.Name;
            line.DeparturePrice = date.PriceOverride ?? date.TourPrice?.Price ?? 0m;
            line.GuestIds = [.. line.GuestIds.Where(id => !id.Equals(guest.Id, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase)];
            if (line.GuestIds.FirstOrDefault(id => !knownGuests.Contains(id)) is { } missing)
                throw new BusinessRuleException("A guest on the booking no longer exists. Remove them and add them again.");

            var package = line.PackageKey is { } key ? packagesByKey.GetValueOrDefault(key) : null;

            if (savedItems.TryGetValue(line.Key, out var item))
            {
                line.IsNew = false;
                line.SavedAmount = item.Amount;
                line.SavedTourDateId = item.TourDateId;
                line.SavedPackageKey = item.BookingPackageId;
                line.SavedSpecialPrice = item.BookingPackageId != null || await WasPackPricedAsync(booking, item, cancellationToken);
                line.Cancelled = item.Cancelled == true;
                itemsByLine[line] = item;
                if (line.Cancelled)
                {
                    line.GuestIds = [.. (item.Guests ?? []).Where(g => g.DeletedOn == null).Select(g => g.GuestId.ToString())];
                    continue; // a cancelled tour stays as it was
                }

                var before = BookingStatuses.GuestCount(item);
                var after = line.GuestCount;
                var moved = item.TourDateId != line.TourDateId;
                var savedGuests = (item.Guests ?? []).Where(g => g.DeletedOn == null).Select(g => g.GuestId.ToString())
                    .Where(id => !id.Equals(guest.Id, StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (tourAndGuestsLocked && (item.TourId != date.TourId || !savedGuests.SetEquals(line.GuestIds)))
                    throw new BusinessRuleException("This booking is paid: only an administrator can change a tour or its guests.");
                if (departureLocked && (moved || (item.PickupLocation ?? "") != (line.PickupLocation ?? "")))
                    throw new BusinessRuleException("This booking is refunded: its tours' departures and pick ups can't be changed.");

                if (moved)
                {
                    await capacity.ReleaseAsync(item.TourDateId, before, cancellationToken);
                    await capacity.TakeAsync(date.Id, after, cancellationToken);
                }
                else if (after > before)
                {
                    await capacity.TakeAsync(date.Id, after - before, cancellationToken);
                }
                else if (after < before)
                {
                    await capacity.ReleaseAsync(date.Id, before - after, cancellationToken);
                }

                SyncGuests(item, line.GuestIds);
                item.Update(date.StartDate, date.EndDate, null, null, null, date.TourId, date.Id, null, null, null,
                    line.PickupLocation, line.WaiverSigned, tour.TourCategoryId, bookedBy, null, null, null, null, null);
                item.BookingPackage = package;
            }
            else
            {
                if (locked)
                    throw new BusinessRuleException("This booking is paid: tours can't be added to it.");
                line.IsNew = true;
                await capacity.TakeAsync(date.Id, line.GuestCount, cancellationToken);
                item = new BookingItem(date.StartDate, date.EndDate, 0, null, null, date.TourId, date.Id, null, null,
                    line.PickupLocation, line.WaiverSigned, tour.TourCategoryId, bookedBy)
                {
                    Guests = [.. line.GuestIds.Select(id => new BookingItemGuest(DefaultIdType.Parse(id)))],
                    BookingPackage = package,
                };
                booking.Items.Add(item);
                db.BookingItems.Add(item);
                itemsByLine[line] = item;
            }
        }

        // Prices ---------------------------------------------------------------------------------
        PackOption? pack = null;
        if (input.PackIncentiveId is { } packId)
        {
            var packEntity = await db.PackIncentives.AsNoTracking().Include(p => p.Tours).Include(p => p.Tiers)
                .FirstOrDefaultAsync(p => p.Id == packId, cancellationToken)
                ?? throw new BusinessRuleException("The pack incentive on this booking no longer exists. Remove it and save again.");
            pack = BookingPacks.ToOption(packEntity);
            if (!BookingPricing.Qualifies(pack, input.Lines))
                throw new BusinessRuleException(
                    $"{pack.Name} needs {pack.MinimumTours} of its tours on the booking; it has {BookingPricing.EligibleTours(pack, input.Lines)}. Add tours or remove the pack.");
            if (pack.Tiers.Count > 0 && pack.Tiers.All(t => t.Id != input.PackIncentiveTierId))
                input.PackIncentiveTierId = pack.Tiers[0].Id;
        }

        BookingPricing.Apply(input.Lines, pack, input.PackIncentiveTierId, input.Packages, packageOptions);
        // Cancelled tours too, as legacy: they keep their share of a pack, so the items still add up to the total.
        foreach (var (line, item) in itemsByLine)
            item.Update(null, null, line.Amount, null, null, null, null, null, null, null,
                item.PickupLocation, item.WaiverSigned, item.TourCategoryId, bookedBy, null, null, null, null, null);
        var total = BookingPricing.Total(input.Lines);

        // A paid booking whose total changed ------------------------------------------------------
        var keepAmount = false;
        decimal? outstanding = null;
        if (!creating && booking.IsPaid && total != booking.TotalAmount)
        {
            var difference = total - booking.TotalAmount;
            switch (command.PaidChange)
            {
                case PaidTotalChange.ChargeDifference when difference > 0:
                    outstanding = (booking.AmountOutstanding ?? 0) + difference;
                    break;
                case PaidTotalChange.KeepAmountPaid:
                    keepAmount = true;
                    break;
                case PaidTotalChange.Refund when difference < 0:
                    if (command.RefundAmount > -difference)
                        throw new BusinessRuleException($"The refund can't be more than the {-difference:$#,##0.00} the total went down by.");
                    if (command.RefundAmount > 0)
                        await refunds.RefundBookingAsync(booking, command.RefundAmount, partial: true, cancellationToken);
                    else
                        keepAmount = true;
                    break;
                default:
                    throw new BusinessRuleException(
                        $"This booking is paid and its total changes from {booking.TotalAmount:$#,##0.00} to {total:$#,##0.00}. Choose how to handle the difference.");
            }
        }

        booking.Update(input.Description, total, "USD", itemsByLine.Count, outstanding is not null ? false : null, null, guest.Id, null, null,
            guest.Name, guest.Email, input.AdditionalNotes, input.BookingSource, null, null, bookedBy, keepAmount, outstanding);
        booking.SetGuestEmail(guest.Email); // as legacy, always the lead guest's: a guest without one doesn't inherit the last one's
        booking.PackIncentiveId = pack?.Id;
        booking.PackIncentiveTierId = pack is null ? null : input.PackIncentiveTierId;

        ApplyPaymentMethod(booking, input, creating, abilities);
        booking.ConcurrencyVersion++;
        await db.SaveChangesAsync(cancellationToken);
        return booking.Id;
    }

    /// <summary>Who the booking is credited to (and so its tours, for staff sales).</summary>
    private DefaultIdType BookedBy(BookingEditor input, Booking booking, bool creating, BookingAbilities abilities)
    {
        if (creating)
            return user.UserId ?? DefaultIdType.Empty;
        if (input.DirectFromWebsite || string.IsNullOrEmpty(input.BookedById) || !DefaultIdType.TryParse(input.BookedById, out var chosen))
            return booking.CreatedBy;
        if (chosen != booking.CreatedBy && !abilities.CanChangeSalesPerson)
            throw new ForbiddenException("You can't change who a booking is credited to.");
        return chosen;
    }

    private async Task<bool> WasPackPricedAsync(Booking booking, BookingItem item, CancellationToken cancellationToken) =>
        booking.PackIncentiveId is { } packId && item.TourId is { } tourId &&
        await db.PackIncentiveTours.AnyAsync(t => t.PackIncentiveId == packId && t.TourId == tourId, cancellationToken);

    private void SyncGuests(BookingItem item, IReadOnlyCollection<string> guestIds)
    {
        item.Guests ??= [];
        foreach (var guest in item.Guests.Where(g => g.DeletedOn == null && !guestIds.Contains(g.GuestId.ToString(), StringComparer.OrdinalIgnoreCase)).ToList())
            db.BookingItemGuests.Remove(guest); // soft delete
        var current = item.Guests.Where(g => g.DeletedOn == null).Select(g => g.GuestId.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in guestIds.Where(id => !current.Contains(id)))
        {
            var added = new BookingItemGuest(DefaultIdType.Parse(id));
            item.Guests.Add(added);
            db.BookingItemGuests.Add(added);
        }
    }

    /// <summary>
    /// Cash on a new booking means it was paid there and then. Changing the method later (legacy: only for a
    /// few staff, or administrators within an hour of booking) records or undoes that cash payment.
    /// </summary>
    private void ApplyPaymentMethod(Booking booking, BookingEditor input, bool creating, BookingAbilities abilities)
    {
        if (creating)
        {
            if (input.PaymentMethod == BookingPaymentMethod.Cash)
                AddCash(booking, booking.TotalAmount);
            return;
        }

        var latest = booking.Payments.OrderByDescending(p => p.CreatedOn).FirstOrDefault();
        var saved = latest?.PaymentMethod ?? BookingPaymentMethod.Stripe;
        if (input.PaymentMethod == saved)
            return;
        var recent = booking.BookingDate >= clock.Now.AddHours(-1);
        if (!abilities.CanFlagAsPaid && !(abilities.IsAdmin && recent))
            throw new ForbiddenException("You can't change this booking's payment method.");

        if (input.PaymentMethod == BookingPaymentMethod.Cash)
        {
            if (BookingStatuses.Outstanding(booking) is > 0 and var owed && booking.Refunded != true)
                AddCash(booking, owed);
            else
                latest?.SetPaymentMethod(BookingPaymentMethod.Cash);
        }
        else if (latest is { PaymentMethod: BookingPaymentMethod.Cash } && latest.Amount == booking.TotalAmount)
        {
            db.BookingPayments.Remove(latest);
            booking.FlagBookingAsUnPaid();
        }
        else
        {
            latest?.SetPaymentMethod(BookingPaymentMethod.Stripe);
        }
    }

    private void AddCash(Booking booking, decimal amount)
    {
        var payment = new BookingPayment(booking.Id, amount, "", false, booking.GuestName, booking.GuestEmail, booking.CurrencyCode, BookingPaymentMethod.Cash)
        {
            PaymentDate = clock.Now,
        };
        booking.Payments.Add(payment);
        db.BookingPayments.Add(payment);
        booking.ApplyManualPayment(amount);
    }
}
