using Microsoft.AspNetCore.Identity;
using Fuse.Application.Common.Time;
using Fuse.Application.Features.Bookings;
using Fuse.Application.Features.Identity;
using Fuse.Contracts.Identity;
using Fuse.Contracts.Site;
using Fuse.Domain.Catalog.Bookings;
using Fuse.Domain.Identity;

namespace Fuse.Application.Features.Site;

// The website's basket and checkout. Legacy trusted the prices the browser sent and signed visitors in to any
// account from its email alone; here the server prices everything and never signs anyone in.

/// <summary>Current prices and whether each departure can still be booked as asked (the website's prices only).</summary>
internal static class BasketPricing
{
    /// <param name="staff">Through a staff member's QR code: tours shown only by QR code, and prices not published to the website, can be booked too.</param>
    public static async Task<BasketQuote> QuoteAsync(IAppDbContext db, IBusinessClock clock, IReadOnlyList<BasketLineRequest> lines, CancellationToken cancellationToken,
        bool staff = false)
    {
        var ids = lines.Select(l => l.DepartureId).Distinct().ToList();
        var departures = await db.TourDates.AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .Select(d => new
            {
                d.Id, d.TourId, d.StartDate, d.EndDate, d.AvailableSpaces,
                Price = d.PriceOverride ?? d.TourPrice.Price,
                PriceTitle = d.TourPrice.Title,
                // As legacy: in-house prices never; through a staff QR code, prices not published to the website too.
                OnWebsite = d.TourPrice.InHouseOnly != true && (staff || d.TourPrice.PublishToWebsite == true),
            })
            .ToDictionaryAsync(d => d.Id, cancellationToken);
        var tourIds = departures.Values.Select(d => d.TourId).Distinct().ToList();
        var tours = await db.Tours.AsNoTracking().Where(t => tourIds.Contains(t.Id)).Where(SiteBookableTours.Rule(db, staff))
            .Select(t => new { t.Id, t.Name, t.UrlSlug, t.ImagePath })
            .ToDictionaryAsync(t => t.Id, cancellationToken);
        var pickups = (await db.TourPickupLocations.AsNoTracking().Where(l => tourIds.Contains(l.TourId))
                .Select(l => new { l.TourId, l.Property.Name }).ToListAsync(cancellationToken))
            .ToLookup(l => l.TourId, l => l.Name);
        var now = clock.Now;

        return new BasketQuote(
        [
            .. lines.Select(line =>
            {
                if (!departures.TryGetValue(line.DepartureId, out var departure) || !departure.OnWebsite
                    || !tours.TryGetValue(departure.TourId, out var tour))
                    return new BasketQuoteLine { DepartureId = line.DepartureId, Guests = line.Guests, Problem = "This departure is no longer available." };

                var points = pickups[tour.Id].Order().ToList();
                var pickup = points.Count == 1 ? points[0] : points.FirstOrDefault(p => p == line.Pickup);
                var problem =
                    departure.StartDate <= now ? "This departure has already left."
                    : departure.AvailableSpaces <= 0 ? "Sold out."
                    : departure.AvailableSpaces < line.Guests ? $"Only {departure.AvailableSpaces} place{(departure.AvailableSpaces == 1 ? "" : "s")} left."
                    : points.Count > 1 && pickup is null ? "Choose a pick-up location."
                    : null;
                return new BasketQuoteLine
                {
                    DepartureId = departure.Id,
                    TourId = tour.Id,
                    TourName = tour.Name,
                    TourSlug = SiteCache.Slug(tour.UrlSlug, tour.Name),
                    ImagePath = tour.ImagePath,
                    Start = departure.StartDate,
                    End = departure.EndDate,
                    PriceTitle = departure.PriceTitle,
                    UnitPrice = departure.Price,
                    Guests = line.Guests,
                    Pickup = pickup,
                    Pickups = points,
                    Problem = problem,
                };
            }),
        ]);
    }
}

internal sealed class QuoteBasketHandler(IAppDbContext db, IBusinessClock clock) : IQueryHandler<QuoteBasket, BasketQuote>
{
    public async ValueTask<BasketQuote> Handle(QuoteBasket query, CancellationToken cancellationToken) =>
        await BasketPricing.QuoteAsync(db, clock, query.Lines, cancellationToken,
            staff: await StaffReferrals.FindAsync(db, query.StaffId, cancellationToken) is not null);
}

internal sealed class PlaceBookingHandler(
    IAppDbContext db,
    UserManager<ApplicationUser> users,
    ICurrentUser currentUser,
    TourCapacity capacity,
    BookingCheckouts checkouts,
    UnpaidCheckouts unpaid,
    IBusinessClock clock) : ICommandHandler<PlaceBooking, PlacedBooking>
{
    public async ValueTask<PlacedBooking> Handle(PlaceBooking command, CancellationToken cancellationToken)
    {
        if (command.Replaces is { } earlier)
            await unpaid.ReplaceAsync(earlier, command.Contact.Email, cancellationToken);

        // Through a staff member's QR code: credited to them (staff sales), and their extra tours and prices allowed.
        var staff = await StaffReferrals.FindAsync(db, command.StaffId, cancellationToken);

        // Priced here, from the departures as they are now.
        var quote = await BasketPricing.QuoteAsync(db, clock,
            [.. command.Lines.Select(l => new BasketLineRequest(l.DepartureId, l.GuestKeys.Count + 1, l.Pickup))], cancellationToken, staff is not null);
        if (quote.Lines.FirstOrDefault(l => l.Problem is not null) is { } problem)
            throw new BusinessRuleException($"{(problem.TourName.Length > 0 ? $"{problem.TourName}: " : "")}{problem.Problem} Update your basket and try again.");

        var lead = await LeadGuestAsync(command, cancellationToken);
        var others = await OtherGuestsAsync(command, lead.Id, cancellationToken);
        var leadId = DefaultIdType.Parse(lead.Id);
        var name = $"{lead.FirstName} {lead.LastName}".Trim();

        var booking = new Booking($"Web Booking: {quote.Lines.Count} Tour{(quote.Lines.Count == 1 ? "" : "s")}", quote.Total, "USD", quote.Lines.Count,
            false, clock.Now, lead.Id, null, false, name, lead.Email, string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim(),
            // As legacy: no waiver yet, and nothing outstanding until a payment is taken (a balance reads "Partially Paid").
            "Website", null, null)
        {
            Items = [],
            Payments = [],
            // Booked by the guest themselves, which is what marks it direct from the website; or credited to the staff
            // member whose QR code they came through.
            OverrideCreatedBy = true,
            CreatedBy = staff?.Id ?? leadId,
        };
        var categories = await db.Tours.AsNoTracking().Where(t => quote.Lines.Select(l => l.TourId).Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.TourCategoryId, cancellationToken);
        foreach (var (line, request) in quote.Lines.Zip(command.Lines))
        {
            await capacity.TakeAsync(line.DepartureId, line.Guests, cancellationToken);
            var item = new BookingItem(line.Start, line.End, line.UnitPrice, null, null, line.TourId, line.DepartureId, null, null,
                line.Pickup, false, categories.GetValueOrDefault(line.TourId), staff?.Id ?? leadId)
            {
                Guests = [.. request.GuestKeys.Select(key => new BookingItemGuest(others[key]))],
            };
            booking.Items.Add(item);
        }
        db.Bookings.Add(booking);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            // As legacy: payable for Stripe's default 24 hours; if never paid it stays Unpaid for staff (UnpaidCheckouts).
            var (session, _) = await checkouts.CreateAsync(booking, cancellationToken, new CheckoutOptions(WebsiteCheckout: true));
            booking.SetStripeSessonId(session.Id);
            await db.SaveChangesAsync(cancellationToken);
            return new PlacedBooking(booking.Id, session.Url);
        }
        catch
        {
            // No way to pay: give the places back rather than leave an unpayable booking holding them.
            foreach (var item in booking.Items)
                await capacity.ReleaseAsync(item.TourDateId, BookingStatuses.GuestCount(item), CancellationToken.None);
            db.Bookings.Remove(booking);
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// The signed-in visitor; else the account with this email (used as it is: never signed in or changed);
    /// else a new guest account, with a password only when they chose to create one.
    /// </summary>
    private async Task<ApplicationUser> LeadGuestAsync(PlaceBooking command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is { } signedIn && await users.FindByIdAsync(signedIn.ToString()) is { } me)
            return me;
        var contact = command.Contact;
        if (await users.FindByEmailAsync(contact.Email.Trim()) is { } existing)
            return existing;

        var user = IdentityResults.NewUser(new UserProfile
        {
            FirstName = contact.FirstName.Trim(),
            LastName = contact.LastName.Trim(),
            Email = contact.Email.Trim(),
            PhoneNumber = contact.PhoneNumber.Trim(),
            DateOfBirth = contact.DateOfBirth,
            Gender = contact.Gender,
            Nationality = contact.Nationality,
        });
        var created = string.IsNullOrEmpty(command.Password) ? await users.CreateAsync(user) : await users.CreateAsync(user, command.Password);
        created.ThrowIfFailed(property: "Password");
        (await users.AddToRoleAsync(user, FuseRoles.Guest)).ThrowIfFailed();
        return user;
    }

    /// <summary>An account for each other guest (one per person, whichever tours they're on); a known email reuses its account.</summary>
    private async Task<Dictionary<string, DefaultIdType>> OtherGuestsAsync(PlaceBooking command, string leadId, CancellationToken cancellationToken)
    {
        var used = command.Lines.SelectMany(l => l.GuestKeys).ToHashSet();
        var ids = new Dictionary<string, DefaultIdType>();
        foreach (var guest in command.Guests.Where(g => used.Contains(g.Key)))
        {
            var email = string.IsNullOrWhiteSpace(guest.Email) ? null : guest.Email.Trim();
            var user = email is null ? null : await users.FindByEmailAsync(email);
            if (user is null)
            {
                user = IdentityResults.NewUser(new UserProfile
                {
                    FirstName = guest.FirstName.Trim(),
                    LastName = guest.LastName.Trim(),
                    Email = email, // none: NewUser gives a placeholder (PlaceholderEmails)
                    PhoneNumber = guest.PhoneNumber?.Trim(),
                    DateOfBirth = guest.DateOfBirth,
                    Gender = guest.Gender,
                    Nationality = guest.Nationality,
                });
                (await users.CreateAsync(user)).ThrowIfFailed();
                (await users.AddToRoleAsync(user, FuseRoles.Guest)).ThrowIfFailed();
            }
            if (user.Id.Equals(leadId, StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException($"{guest.FirstName} {guest.LastName} has the booker's email address. Give them their own, or leave it blank.");
            ids[guest.Key] = DefaultIdType.Parse(user.Id);
        }
        return ids;
    }
}
