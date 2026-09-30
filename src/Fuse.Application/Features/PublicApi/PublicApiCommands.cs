using Microsoft.AspNetCore.Identity;
using Fuse.Application.Common.Payments;
using Fuse.Application.Common.Time;
using Fuse.Application.Features.Bookings;
using Fuse.Application.Features.Identity;
using Fuse.Contracts.Bookings;
using Fuse.Contracts.Identity;
using Fuse.Domain.Catalog.Bookings;
using Fuse.Domain.Identity;

namespace Fuse.Application.Features.PublicApi;

// The public booking API's changes (legacy POST/PUT /api/users, POST /api/bookings and its payment calls), with
// legacy's rules and error messages. Problems are thrown as the app's usual exceptions; the API's endpoints turn
// them into legacy's responses (RequestValidationException → 400 with field errors, ConflictException → 409,
// NotFoundException → 404, BusinessRuleException → 400 with the message).

/// <summary>A guest (<see cref="CreateUserApiRequest.IsGuest"/>) or a Basic staff member; the new user's id.</summary>
public sealed record CreateApiUser(CreateUserApiRequest Request) : ICommand<string>;

public sealed record UpdateApiUser(string Id, UpdateUserApiRequest Request) : ICommand;

/// <param name="StaffUserId">Who the booking is credited to: the calling client's service user (or its dev user).</param>
public sealed record CreateApiBooking(CreateBookingApiRequest Request, DefaultIdType StaffUserId) : ICommand<DefaultIdType>;

/// <summary>Legacy FlagBookingAsPaid: paid, confirmed, nothing outstanding, and the Stripe session (null when none sent).</summary>
public sealed record FlagApiBookingAsPaid(DefaultIdType Id, string? StripeSessionId) : ICommand;

public enum ApiCheckoutError
{
    None,
    BookingNotFound,
    NotStripe,
    MissingEmail,
}

public sealed record ApiCheckout(string? Url, ApiCheckoutError Error);

/// <param name="TestMode">Stripe's test mode (the request's isDevelopment), whatever the environment.</param>
public sealed record CreateApiCheckout(DefaultIdType BookingId, bool TestMode = false, string? SuccessUrl = null, string? CancelUrl = null)
    : ICommand<ApiCheckout>;

// Users ----------------------------------------------------------------------------------------------------------

internal static class ApiUserProfiles
{
    public static void Apply(ApplicationUser user, string? firstName, string? lastName, string? email, string? phoneNumber, DateTime? dateOfBirth,
        string? gender, string? nationality, string? address, string? city, string? zipPostalCode, string? state)
    {
        // Passport and visa details aren't part of the API: keep what the user has.
        user.Apply(new UserProfile
        {
            FirstName = firstName ?? "",
            LastName = lastName ?? "",
            Email = email,
            PhoneNumber = phoneNumber,
            DateOfBirth = dateOfBirth,
            Gender = gender,
            Nationality = nationality,
            Address = address,
            City = city,
            ZipPostalCode = zipPostalCode,
            PassportNumber = user.PassportNumber,
            PassportIssueDate = user.PassportIssueDate,
            PassportExpiryDate = user.PassportExpiryDate,
            PassportIssuingCountry = user.PassportIssuingCountry,
            VisaIssueDate = user.VisaIssueDate,
            VisaExpiryDate = user.VisaExpiryDate,
        });
        user.State = state;
    }

    /// <summary>Identity's refusal as legacy's 400 ("The user could not be created." with the reasons).</summary>
    public static void ThrowIfFailed(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new BusinessRuleException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}

/// <summary>Legacy CreateUserRequestValidator's rules and messages.</summary>
internal sealed class CreateApiUserValidator : AbstractValidator<CreateApiUser>
{
    public CreateApiUserValidator(IAppDbContext db)
    {
        When(x => !string.IsNullOrWhiteSpace(x.Request.Email), () =>
            RuleFor(x => x.Request.Email).Cascade(CascadeMode.Stop)
                .EmailAddress().WithMessage("Invalid Email Address.")
                .MustAsync(async (email, cancellationToken) => !await db.Users.AnyAsync(u => u.NormalizedEmail == email!.ToUpper(), cancellationToken))
                .WithMessage((_, email) => $"Email {email} is already registered.")
                .OverridePropertyName("Email"));
        RuleFor(x => x.Request.FirstName).NotEmpty().WithMessage("'First Name' must not be empty.").OverridePropertyName("FirstName");
        RuleFor(x => x.Request.LastName).NotEmpty().WithMessage("'Last Name' must not be empty.").OverridePropertyName("LastName");
        When(x => !x.Request.IsGuest, () =>
        {
            RuleFor(x => x.Request.Password).Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("'Password' must not be empty.")
                .MinimumLength(6).WithMessage((x, password) => $"The length of 'Password' must be at least 6 characters. You entered {password?.Length ?? 0} characters.")
                .OverridePropertyName("Password");
            RuleFor(x => x.Request.ConfirmPassword).Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("'Confirm Password' must not be empty.")
                .Equal(x => x.Request.Password).WithMessage("'Confirm Password' must be equal to 'Password'.")
                .OverridePropertyName("ConfirmPassword");
        });
    }
}

internal sealed class CreateApiUserHandler(UserManager<ApplicationUser> users) : ICommandHandler<CreateApiUser, string>
{
    public async ValueTask<string> Handle(CreateApiUser command, CancellationToken cancellationToken)
    {
        var r = command.Request;
        var user = new ApplicationUser
        {
            IsActive = true,
            EmailConfirmed = true,
            LockoutEnabled = true,
            SignUpDate = DateTime.UtcNow,
            RefreshTokenExpiryTime = DateTime.UtcNow,
        };
        ApiUserProfiles.Apply(user, r.FirstName, r.LastName, r.Email, r.PhoneNumber, r.DateOfBirth, r.Gender, r.Nationality, r.Address, r.City,
            r.ZipPostalCode, r.State);
        user.UserName = user.Email;
        // Guests may be created without a password (they can set one later), as legacy.
        ApiUserProfiles.ThrowIfFailed(string.IsNullOrEmpty(r.Password) ? await users.CreateAsync(user) : await users.CreateAsync(user, r.Password));
        // As legacy: a guest when asked, otherwise a Basic staff member.
        ApiUserProfiles.ThrowIfFailed(await users.AddToRoleAsync(user, r.IsGuest ? FuseRoles.Guest : FuseRoles.Basic));
        return user.Id;
    }
}

internal sealed class UpdateApiUserHandler(IAppDbContext db, UserManager<ApplicationUser> users) : ICommandHandler<UpdateApiUser>
{
    public async ValueTask<Unit> Handle(UpdateApiUser command, CancellationToken cancellationToken)
    {
        var r = command.Request;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.Id, cancellationToken) ?? throw new NotFoundException("User not found.");
        ApiUserProfiles.Apply(user, r.FirstName, r.LastName, r.Email, r.PhoneNumber, r.DateOfBirth, r.Gender, r.Nationality, r.Address, r.City,
            r.ZipPostalCode, r.State);
        ApiUserProfiles.ThrowIfFailed(await users.UpdateAsync(user));
        return Unit.Value;
    }
}

// Bookings -------------------------------------------------------------------------------------------------------

internal sealed class CreateApiBookingHandler(IAppDbContext db, TourCapacity capacity, IBusinessClock clock) : ICommandHandler<CreateApiBooking, DefaultIdType>
{
    public async ValueTask<DefaultIdType> Handle(CreateApiBooking command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        Validate(request);

        // A pack mark must be a real pack incentive.
        var pack = request.PackIncentiveId is { } packId
            ? await db.PackIncentives.AsNoTracking().Include(p => p.Tours).Include(p => p.Tiers).FirstOrDefaultAsync(p => p.Id == packId, cancellationToken)
              ?? throw new RequestValidationException("PackIncentiveId", $"No pack incentive was found with id '{request.PackIncentiveId}'.")
            : null;

        // The booker and every traveller must already exist.
        var booker = await db.Users.AsNoTracking().Where(u => u.Id == request.GuestId)
                         .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email }).FirstOrDefaultAsync(cancellationToken)
                     ?? throw new RequestValidationException("GuestId", $"No guest was found with id '{request.GuestId}'.");
        var travellerIds = request.Items.SelectMany(i => i.GuestIds ?? []).Concat(request.Packages.SelectMany(p => p.GuestIds ?? []))
            .Select(g => g?.Trim()).Where(g => !string.IsNullOrEmpty(g)).Select(g => g!).Distinct().ToList();
        var knownTravellers = (await db.Users.Where(u => travellerIds.Contains(u.Id)).Select(u => u.Id).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = travellerIds.Where(id => !knownTravellers.Contains(id)).ToList();
        if (missing.Count > 0)
            throw new RequestValidationException("items.guestIds", $"These guest ids were not found: {string.Join(", ", missing)}.");

        var lines = await LooseToursAsync(request, cancellationToken);
        var (packageLines, packageDefinitions) = await PackagesAsync(request, lines, cancellationToken);

        // Enough spaces on every departure (checked before anything is taken, so one 409 names every shortfall).
        var dateIds = lines.Select(l => l.TourDateId).Distinct().ToList();
        var spaces = await db.TourDates.AsNoTracking().Where(d => dateIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, cancellationToken);
        var shortfalls = lines.GroupBy(l => l.TourDateId)
            .Select(g => (Date: spaces[g.Key], Wanted: g.Sum(l => l.GuestCount)))
            .Where(x => x.Date.AvailableSpaces < x.Wanted)
            .Select(x => $"{x.Date.StartDate:dd/MM/yyyy}: {x.Wanted} guest(s) requested but only {x.Date.AvailableSpaces} space(s) remain.")
            .ToList();
        if (shortfalls.Count > 0)
            throw new ConflictException(string.Join(" ", shortfalls));

        // Prices: the pack (when the booking qualifies and a tier is chosen), a package's tier price, else the departure's.
        var packOption = pack is null ? null : BookingPacks.ToOption(pack);
        BookingPricing.Apply(lines, packOption, request.PackIncentiveTierId, packageLines, [.. packageDefinitions.Select(BookingPackages.ToOption)]);

        // "App Booking: 2 Tours, 1 Package"; test bookings are tagged and carry a warning.
        var parts = new List<string>();
        if (request.Items.Count > 0)
            parts.Add($"{request.Items.Count} Tour{(request.Items.Count == 1 ? "" : "s")}");
        if (request.Packages.Count > 0)
            parts.Add($"{request.Packages.Count} Package{(request.Packages.Count == 1 ? "" : "s")}");
        var description = $"App Booking: {(parts.Count > 0 ? string.Join(", ", parts) : "Booking")}" + (request.IsDevelopment ? " [TEST]" : "");
        var notes = request.IsDevelopment
            ? "*** TEST / DEVELOPMENT BOOKING — DO NOT ACTION ***" + (string.IsNullOrWhiteSpace(request.AdditionalNotes) ? "" : $"\n\n{request.AdditionalNotes}")
            : request.AdditionalNotes;
        var waiverSigned = request.Items.All(i => i.WaiverSigned) && request.Packages.All(p => p.Tours.All(t => t.WaiverSigned));

        var total = BookingPricing.Total(lines); // as legacy: pack tours at their exact share, then to the cent
        var booking = new Booking(description, total, "USD", lines.Sum(l => l.GuestCount), false, clock.Now, booker.Id, null, waiverSigned,
            $"{booker.FirstName} {booker.LastName}".Trim(), booker.Email, notes, ApiBookings.Source, null, null)
        {
            Items = [],
            Payments = [],
            OverrideCreatedBy = true,
            CreatedBy = command.StaffUserId,
            PackIncentiveId = request.PackIncentiveId,
            PackIncentiveTierId = request.PackIncentiveTierId,
        };
        db.Bookings.Add(booking);

        var packages = new Dictionary<DefaultIdType, BookingPackage>();
        foreach (var packageLine in packageLines)
        {
            var definition = packageDefinitions.First(p => p.Id == packageLine.PackageId);
            var added = new BookingPackage(definition.Id, definition.Name, packageLine.Price, packageLine.TierTitle, definition.CommissionAmount,
                definition.CommissionIgnoresMinimumPax, command.StaffUserId);
            booking.BookingPackages.Add(added);
            db.BookingPackages.Add(added);
            packages[packageLine.Key] = added;
        }

        var tourCategories = await db.Tours.AsNoTracking().Where(t => lines.Select(l => l.TourId).Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.TourCategoryId, cancellationToken);
        foreach (var line in lines)
        {
            var date = await capacity.TakeAsync(line.TourDateId, line.GuestCount, cancellationToken);
            var item = new BookingItem(date.StartDate, date.EndDate, line.Amount, null, null, date.TourId, date.Id, null, null,
                line.PickupLocation, line.WaiverSigned, tourCategories.GetValueOrDefault(date.TourId), command.StaffUserId)
            {
                Guests = [.. line.GuestIds.Select(id => new BookingItemGuest(DefaultIdType.Parse(id)))],
                BookingPackage = line.PackageKey is { } key ? packages[key] : null,
            };
            booking.Items.Add(item);
            db.BookingItems.Add(item);
        }

        // As legacy: cash is taken there and then, so the booking is paid.
        if (request.PaymentMethod == BookingPaymentMethod.Cash)
        {
            booking.FlagBookingAsPaid();
            var payment = new BookingPayment(booking.Id, booking.TotalAmount, "", false, booking.GuestName, booking.GuestEmail, booking.CurrencyCode,
                BookingPaymentMethod.Cash) { PaymentDate = clock.Now };
            booking.Payments.Add(payment);
            db.BookingPayments.Add(payment);
        }

        await db.SaveChangesAsync(cancellationToken);
        return booking.Id;
    }

    private static void Validate(CreateBookingApiRequest r)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var list))
                errors[key] = list = [];
            list.Add(message);
        }

        if (string.IsNullOrWhiteSpace(r.GuestId))
            Add("GuestId", "A guest is required. Find or create the guest first, then send their id.");
        if (!Enum.IsDefined(r.PaymentMethod))
            Add("PaymentMethod", "Payment method must be either Stripe or Cash.");
        if (r.Items.Count == 0 && r.Packages.Count == 0)
            Add("Items", "At least one tour item or package is required.");

        for (var index = 0; index < r.Items.Count; index++)
        {
            var item = r.Items[index];
            if (!item.TourId.HasValue)
                Add($"Items[{index}].TourId", "A tour is required.");
            if (!item.TourDateId.HasValue)
                Add($"Items[{index}].TourDateId", "A tour date is required.");
        }

        for (var index = 0; index < r.Packages.Count; index++)
        {
            var package = r.Packages[index];
            var prefix = $"Packages[{index}]";
            if (!package.PackageId.HasValue)
                Add($"{prefix}.PackageId", "A package is required.");
            if (!package.TierId.HasValue)
                Add($"{prefix}.TierId", "A price tier is required.");
            if (package.Tours.Count == 0)
                Add($"{prefix}.Tours", "A departure is required for each tour in the package.");
            for (var t = 0; t < package.Tours.Count; t++)
            {
                if (!package.Tours[t].TourId.HasValue)
                    Add($"{prefix}.Tours[{t}].TourId", "A tour is required.");
                if (!package.Tours[t].TourDateId.HasValue)
                    Add($"{prefix}.Tours[{t}].TourDateId", "A tour date is required.");
            }
            var duplicated = package.Tours.Where(t => t.TourId.HasValue).GroupBy(t => t.TourId!.Value).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicated.Count > 0)
                Add($"{prefix}.Tours", $"Each tour may appear once. Duplicated: {string.Join(", ", duplicated)}.");
        }

        if (errors.Count > 0)
            throw new RequestValidationException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }

    /// <summary>The loose tours, each on a departure of that tour still to come; a 400 names any that isn't.</summary>
    private async Task<List<BookingLine>> LooseToursAsync(CreateBookingApiRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var lines = new List<BookingLine>();
        var dates = await DeparturesAsync(request.Items.Select(i => i.TourDateId!.Value), cancellationToken);
        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            if (!dates.TryGetValue(item.TourDateId!.Value, out var date) || date.TourId != item.TourId)
            {
                errors[$"Items[{index}].TourDateId"] = ["The tour date was not found, is in the past, or does not belong to this tour."];
                continue;
            }
            lines.Add(Line(date, item.GuestIds, item.OtherPickupLocation ?? item.PickupLocation, item.WaiverSigned, null));
        }
        if (errors.Count > 0)
            throw new RequestValidationException(errors);
        return lines;
    }

    /// <summary>Each package's tours (one departure for every tour it lists) at its tier's price, grouped under the package.</summary>
    private async Task<(List<BookingPackageLine> Lines, List<Domain.Catalog.Packages.Package> Definitions)> PackagesAsync(
        CreateBookingApiRequest request, List<BookingLine> lines, CancellationToken cancellationToken)
    {
        if (request.Packages.Count == 0)
            return ([], []);
        var definitions = await BookingPackages.LoadAsync(db, [.. request.Packages.Select(p => p.PackageId!.Value).Distinct()], cancellationToken);
        var dates = await DeparturesAsync(request.Packages.SelectMany(p => p.Tours).Select(t => t.TourDateId!.Value), cancellationToken);
        var packageLines = new List<BookingPackageLine>();

        for (var index = 0; index < request.Packages.Count; index++)
        {
            var requested = request.Packages[index];
            var prefix = $"packages[{index}]";
            var definition = definitions.FirstOrDefault(p => p.Id == requested.PackageId)
                             ?? throw new RequestValidationException($"{prefix}.packageId", $"No package was found with id '{requested.PackageId}'.");
            var tier = definition.PackageTiers?.FirstOrDefault(t => t.Id == requested.TierId)
                       ?? throw new RequestValidationException($"{prefix}.tierId", $"Tier '{requested.TierId}' does not belong to this package.");

            var packageTourIds = (definition.PackageItems ?? []).Select(i => i.TourId).ToHashSet();
            var requestedTourIds = requested.Tours.Select(t => t.TourId!.Value).ToHashSet();
            var unknown = requestedTourIds.Where(id => !packageTourIds.Contains(id)).ToList();
            if (unknown.Count > 0)
                throw new RequestValidationException($"{prefix}.tours", $"These tours are not part of the package: {string.Join(", ", unknown)}.");
            var uncovered = packageTourIds.Where(id => !requestedTourIds.Contains(id)).ToList();
            if (uncovered.Count > 0)
                throw new RequestValidationException($"{prefix}.tours",
                    $"A departure is required for every tour in the package. Missing: {string.Join(", ", uncovered)}.");

            var packageLine = new BookingPackageLine
            {
                Key = DefaultIdType.NewGuid(),
                PackageId = definition.Id,
                PackageName = definition.Name,
                TierId = tier.Id,
                TierTitle = tier.Title,
                Price = ApiPackages.Total(definition, tier.Id),
            };
            packageLines.Add(packageLine);

            foreach (var tour in requested.Tours)
            {
                if (!dates.TryGetValue(tour.TourDateId!.Value, out var date) || date.TourId != tour.TourId)
                    throw new RequestValidationException($"{prefix}.tours",
                        $"The tour date '{tour.TourDateId}' was not found, is in the past, or does not belong to tour '{tour.TourId}'.");
                lines.Add(Line(date, requested.GuestIds, tour.OtherPickupLocation ?? tour.PickupLocation, tour.WaiverSigned, packageLine.Key));
            }
        }
        return (packageLines, definitions);
    }

    /// <summary>Departures still to come (as legacy's booking search), by id.</summary>
    private async Task<Dictionary<DefaultIdType, Domain.Catalog.Tours.TourDate>> DeparturesAsync(IEnumerable<DefaultIdType> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Distinct().ToList();
        var now = clock.Now;
        return await db.TourDates.AsNoTracking().Include(d => d.TourPrice)
            .Where(d => wanted.Contains(d.Id) && d.StartDate > now)
            .ToDictionaryAsync(d => d.Id, cancellationToken);
    }

    /// <summary>A tour on a departure: the lead booker plus <paramref name="guestIds"/> (the other travellers).</summary>
    private static BookingLine Line(Domain.Catalog.Tours.TourDate date, IEnumerable<string>? guestIds, string? pickup, bool waiverSigned, DefaultIdType? packageKey) => new()
    {
        Key = DefaultIdType.NewGuid(),
        IsNew = true,
        TourId = date.TourId,
        TourDateId = date.Id,
        StartDate = date.StartDate,
        EndDate = date.EndDate,
        DeparturePrice = date.PriceOverride ?? date.TourPrice?.Price ?? 0m,
        PickupLocation = pickup,
        WaiverSigned = waiverSigned,
        PackageKey = packageKey,
        GuestIds = [.. (guestIds ?? []).Select(g => g.Trim()).Where(g => g != "")],
    };
}

internal static class ApiBookings
{
    /// <summary>The booking source of every booking made through the API.</summary>
    public const string Source = "Booking App";
}

internal sealed class FlagApiBookingAsPaidHandler(IAppDbContext db, PaidBookingNotifications notifications) : ICommandHandler<FlagApiBookingAsPaid>
{
    public async ValueTask<Unit> Handle(FlagApiBookingAsPaid command, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(b => b.Items).ThenInclude(i => i.Guests)
                          .FirstOrDefaultAsync(b => b.Id == command.Id, cancellationToken)
                      ?? throw new NotFoundException("Booking not found.");
        var wasPaid = booking.IsPaid;
        // Exactly what legacy's FlagBookingAsPaid procedure sets.
        await db.Bookings.Where(b => b.Id == command.Id).ExecuteUpdateAsync(set => set
            .SetProperty(b => b.IsPaid, true)
            .SetProperty(b => b.BookingConfirmed, true)
            .SetProperty(b => b.AmountOutstanding, 0m)
            .SetProperty(b => b.StripeSessionId, command.StripeSessionId), cancellationToken);
        if (!wasPaid)
            await notifications.EnqueueAsync(booking, cancellationToken);
        return Unit.Value;
    }
}

internal sealed class CreateApiCheckoutHandler(IAppDbContext db, BookingCheckouts checkouts) : ICommandHandler<CreateApiCheckout, ApiCheckout>
{
    public async ValueTask<ApiCheckout> Handle(CreateApiCheckout command, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(b => b.Items).ThenInclude(i => i.Guests).Include(b => b.Payments)
            .FirstOrDefaultAsync(b => b.Id == command.BookingId, cancellationToken);
        if (booking is null)
            return new ApiCheckout(null, ApiCheckoutError.BookingNotFound);
        // Legacy's booking payment method: its first payment's, else Stripe.
        var method = booking.Payments.Where(p => p.DeletedOn == null).OrderBy(p => p.CreatedOn).FirstOrDefault()?.PaymentMethod ?? BookingPaymentMethod.Stripe;
        if (method != BookingPaymentMethod.Stripe)
            return new ApiCheckout(null, ApiCheckoutError.NotStripe);
        if (string.IsNullOrWhiteSpace(PlaceholderEmails.Visible(booking.GuestEmail)))
            return new ApiCheckout(null, ApiCheckoutError.MissingEmail);

        // The same checkout as the admin's payment links (legacy's), in test mode and with the caller's return pages if asked.
        var (session, _) = await checkouts.CreateAsync(booking, cancellationToken,
            new CheckoutOptions(TestMode: command.TestMode, SuccessUrl: command.SuccessUrl, CancelUrl: command.CancelUrl, RecordSession: true));
        return new ApiCheckout(session.Url, ApiCheckoutError.None);
    }
}
