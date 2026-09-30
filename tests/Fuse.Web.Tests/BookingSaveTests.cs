using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Admin.Pages.Bookings;
using Fuse.Admin.Pages.Tours;
using Fuse.Contracts.Api;
using Fuse.Contracts.Bookings;
using Fuse.Contracts.Common;
using Fuse.Contracts.Dashboard;
using Fuse.Contracts.Identity;
using Fuse.Contracts.Locations;
using Fuse.Contracts.PackIncentives;
using Fuse.Contracts.Packages;
using Fuse.Contracts.Tours;
using Fuse.Domain.Identity;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Authorization;
using Fuse.Shared.Enums;

namespace Fuse.Web.Tests;

/// <summary>The booking wizard's save: spaces, prices (departure, pack, package) and paid bookings.</summary>
public sealed class BookingSaveTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTime Day(int days, int hour = 9) => TestBookings.BusinessNow.Date.AddDays(days).AddHours(hour);

    /// <summary>A tour at <paramref name="price"/> per person with <paramref name="capacity"/> spaces on each departure.</summary>
    private async Task<(DefaultIdType TourId, IReadOnlyList<DepartureOption> Departures)> TourAsync(IApi api, decimal price, int capacity, params DateTime[] starts)
    {
        var draft = TourDraft.New();
        draft.Details.Name = $"Booking tour {Guid.NewGuid():N}";
        draft.Details.Description = "<p>Tour</p>";
        draft.Details.HourDuration = "2";
        draft.Details.MaxCapacity = capacity;
        draft.Details.LocationIds = [(await api.Send(new ListLocations(), Ct))[0].Id];
        draft.Details.PickupPropertyIds = [Guid.Parse((await api.Send(new ListPickupProperties(), Ct)).Single(p => p.Name == TestApp.SeedPropertyName).Id)];
        draft.SavePrice(null, new TourPriceInput { Title = "Per person", Price = price, HourDuration = 2, PublishToWebsite = true });
        draft.AddDates(starts, [draft.Prices[0].Key], null);
        var id = await api.Send(new CreateTour(draft.ToChanges()), Ct);
        return (id, await api.Send(new ListTourDepartures(id), Ct));
    }

    private async Task<string> GuestAsync(IApi api) =>
        await api.Send(new CreateGuest(new UserProfile { FirstName = "Book", LastName = "Guest", Email = $"guest-{Guid.NewGuid():N}@tests.invalid" }), Ct);

    private static BookingLine Line(DefaultIdType tourId, DepartureOption departure, params string[] guests) => new()
    {
        Key = Guid.NewGuid(),
        IsNew = true,
        TourId = tourId,
        TourDateId = departure.Id,
        StartDate = departure.StartDate,
        PickupLocation = TestApp.SeedPropertyName,
        GuestIds = [.. guests],
    };

    private static BookingEditor NewBooking(string guestId, BookingPaymentMethod method, params BookingLine[] lines) => new()
    {
        Description = "Wizard booking",
        GuestId = guestId,
        BookingSource = "Online",
        PaymentMethod = method,
        Lines = [.. lines],
    };

    private async Task<int> SpacesAsync(DefaultIdType tourDateId)
    {
        await using var scope = TestBookings.TenantScope(app);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().TourDates.Where(d => d.Id == tourDateId).Select(d => d.AvailableSpaces).SingleAsync(Ct);
    }

    private async Task<BookingRow> RowAsync(IApi api, DefaultIdType id)
    {
        var reference = (await api.Send(new GetBookingEditor(id), Ct)).Reference;
        return (await api.Send(new SearchBookings { Keyword = reference.ToString(), PageSize = 100 }, Ct)).Items.Single(r => r.Id == id);
    }

    [Fact]
    public async Task A_new_booking_takes_spaces_and_is_priced_from_its_departure()
    {
        var api = await app.AdminApiAsync();
        var (tourId, departures) = await TourAsync(api, 25, 10, Day(10));
        var (lead, extra) = (await GuestAsync(api), await GuestAsync(api));

        var id = await api.Send(new SaveBooking(NewBooking(lead, BookingPaymentMethod.Stripe, Line(tourId, departures[0], extra))), Ct);

        (await SpacesAsync(departures[0].Id)).ShouldBe(8);
        var editor = await api.Send(new GetBookingEditor(id), Ct);
        editor.Lines.ShouldHaveSingleItem().Amount.ShouldBe(25);
        editor.SavedTotal.ShouldBe(50);
        editor.GuestNames[extra].Name.ShouldBe("Book Guest");
        var row = await RowAsync(api, id);
        (row.Status, row.Outstanding).ShouldBe((BookingState.Unpaid, 50m));
    }

    [Fact]
    public async Task A_cash_booking_is_paid_straight_away()
    {
        var api = await app.AdminApiAsync();
        var (tourId, departures) = await TourAsync(api, 30, 10, Day(11));
        var id = await api.Send(new SaveBooking(NewBooking(await GuestAsync(api), BookingPaymentMethod.Cash, Line(tourId, departures[0]))), Ct);

        var row = await RowAsync(api, id);
        (row.Status, row.Outstanding, row.TotalAmount).ShouldBe((BookingState.Paid, 0m, 30m));
        (await api.Send(new GetBookingOverview(id), Ct)).Payments.ShouldHaveSingleItem().Method.ShouldBe(BookingPaymentMethod.Cash);
        (await api.Send(new GetBookingEditor(id), Ct)).PaymentMethod.ShouldBe(BookingPaymentMethod.Cash);
    }

    [Fact]
    public async Task Editing_moves_spaces_with_guests_departures_and_removed_tours()
    {
        var api = await app.AdminApiAsync();
        var (tourId, departures) = await TourAsync(api, 20, 10, Day(12), Day(13));
        var (other, otherDepartures) = await TourAsync(api, 15, 10, Day(12));
        var lead = await GuestAsync(api);
        var id = await api.Send(new SaveBooking(NewBooking(lead, BookingPaymentMethod.Stripe, Line(tourId, departures[0]))), Ct);

        // Another guest, then the tour moves to the next day, and a second tour is added.
        var editor = await api.Send(new GetBookingEditor(id), Ct);
        var line = editor.Lines.Single();
        line.GuestIds.Add(await GuestAsync(api));
        line.TourDateId = departures[1].Id;
        editor.Lines.Add(Line(other, otherDepartures[0]));
        await api.Send(new SaveBooking(editor), Ct);

        (await SpacesAsync(departures[0].Id)).ShouldBe(10);
        (await SpacesAsync(departures[1].Id)).ShouldBe(8);
        (await SpacesAsync(otherDepartures[0].Id)).ShouldBe(9);
        var saved = await api.Send(new GetBookingEditor(id), Ct);
        saved.SavedTotal.ShouldBe(20 * 2 + 15);
        saved.Lines.Single(l => l.TourId == tourId).StartDate.ShouldBe(Day(13));

        // Taking the first tour off gives its spaces back.
        saved.Lines.RemoveAll(l => l.TourId == tourId);
        await api.Send(new SaveBooking(saved), Ct);
        (await SpacesAsync(departures[1].Id)).ShouldBe(10);
        (await api.Send(new GetBookingEditor(id), Ct)).SavedTotal.ShouldBe(15);
    }

    [Fact]
    public async Task A_departure_without_enough_space_is_refused_and_stale_edits_conflict()
    {
        var api = await app.AdminApiAsync();
        var (tourId, departures) = await TourAsync(api, 20, 1, Day(14));
        var (lead, extra) = (await GuestAsync(api), await GuestAsync(api));

        var full = await Should.ThrowAsync<BusinessRuleException>(() =>
            api.Send(new SaveBooking(NewBooking(lead, BookingPaymentMethod.Stripe, Line(tourId, departures[0], extra))), Ct).AsTask());
        full.Message.ShouldContain("space");
        (await SpacesAsync(departures[0].Id)).ShouldBe(1);

        var id = await api.Send(new SaveBooking(NewBooking(lead, BookingPaymentMethod.Stripe, Line(tourId, departures[0]))), Ct);
        var first = await api.Send(new GetBookingEditor(id), Ct);
        var second = await api.Send(new GetBookingEditor(id), Ct);
        first.AdditionalNotes = "Vegetarian";
        await api.Send(new SaveBooking(first), Ct);
        await Should.ThrowAsync<ConflictException>(() => api.Send(new SaveBooking(second), Ct).AsTask());
    }

    [Fact]
    public async Task A_pack_charges_its_tier_price_split_across_its_tours()
    {
        var api = await app.AdminApiAsync();
        var (tourA, departuresA) = await TourAsync(api, 30, 10, Day(15));
        var (tourB, departuresB) = await TourAsync(api, 35, 10, Day(16));
        var packId = await api.Send(new SavePackIncentive(null, new PackIncentiveInput { Name = $"Pack {Guid.NewGuid():N}", MinimumProductCount = 2, CommissionAmount = 5 },
            [tourA, tourB], [new() { Title = "Adult", Price = 50 }]), Ct);
        var pack = (await api.Send(new GetBookingCatalog(), Ct)).Packs.Single(p => p.Id == packId);
        var lead = await GuestAsync(api);

        var booking = NewBooking(lead, BookingPaymentMethod.Stripe, Line(tourA, departuresA[0]), Line(tourB, departuresB[0])) with
        {
            PackIncentiveId = packId,
            PackIncentiveTierId = pack.Tiers[0].Id,
        };
        var id = await api.Send(new SaveBooking(booking), Ct);
        var saved = await api.Send(new GetBookingEditor(id), Ct);
        saved.Lines.ShouldAllBe(l => l.Amount == 25);
        saved.SavedTotal.ShouldBe(50);
        (await RowAsync(api, id)).HasPackIncentive.ShouldBeTrue();

        // Too few tours for the pack.
        var single = NewBooking(lead, BookingPaymentMethod.Stripe, Line(tourA, departuresA[0])) with { PackIncentiveId = packId };
        (await Should.ThrowAsync<BusinessRuleException>(() => api.Send(new SaveBooking(single), Ct).AsTask())).Message.ShouldContain("needs 2");
    }

    [Fact]
    public async Task A_package_prices_its_tours_and_groups_them()
    {
        var api = await app.AdminApiAsync();
        var (tourA, departuresA) = await TourAsync(api, 30, 10, Day(17));
        var (tourB, departuresB) = await TourAsync(api, 35, 10, Day(18));
        var (adult, child) = (Guid.NewGuid(), Guid.NewGuid());
        var packageId = await api.Send(new SavePackage(null, new PackageChanges
        {
            Details = new PackageInput { Name = $"Combo {Guid.NewGuid():N}" },
            Tiers = [new() { Key = adult, Title = "Adult" }, new() { Key = child, Title = "Child" }],
            Items =
            [
                new() { TourId = tourA, Quantity = 1, Prices = new() { [adult] = 15, [child] = 10 } },
                new() { TourId = tourB, Quantity = 2, Prices = new() { [adult] = 12, [child] = 8 } },
            ],
        }, null, false), Ct);
        var package = (await api.Send(new GetBookingCatalog(), Ct)).Packages.Single(p => p.Id == packageId);
        var key = Guid.NewGuid();
        var lines = new[] { Line(tourA, departuresA[0]), Line(tourB, departuresB[0]), Line(tourB, departuresB[0]) };
        foreach (var line in lines)
            line.PackageKey = key;
        var booking = NewBooking(await GuestAsync(api), BookingPaymentMethod.Stripe, lines) with
        {
            Packages = [new BookingPackageLine { Key = key, PackageId = packageId, PackageName = package.Name, TierId = package.Tiers.Single(t => t.Title == "Adult").Id }],
        };

        var id = await api.Send(new SaveBooking(booking), Ct);

        var saved = await api.Send(new GetBookingEditor(id), Ct);
        saved.Lines.Select(l => l.Amount).Order().ShouldBe([12m, 12m, 15m]);
        saved.SavedTotal.ShouldBe(39);
        saved.Packages.ShouldHaveSingleItem().TierTitle.ShouldBe("Adult");
        (await SpacesAsync(departuresB[0].Id)).ShouldBe(8);
        (await api.Send(new GetBookingOverview(id), Ct)).Items.ShouldAllBe(i => i.PackageName == package.Name);
    }

    [Fact]
    public async Task A_cancelled_pack_tour_keeps_its_share_and_still_counts_toward_the_pack()
    {
        var api = await app.AdminApiAsync();
        var (tourA, departuresA) = await TourAsync(api, 30, 10, Day(20));
        var (tourB, departuresB) = await TourAsync(api, 35, 10, Day(21));
        var (tourC, departuresC) = await TourAsync(api, 40, 10, Day(22));
        var packId = await api.Send(new SavePackIncentive(null, new PackIncentiveInput { Name = $"Pack {Guid.NewGuid():N}", MinimumProductCount = 3, CommissionAmount = 5 },
            [tourA, tourB, tourC], [new() { Title = "Adult", Price = 60 }]), Ct);
        var pack = (await api.Send(new GetBookingCatalog(), Ct)).Packs.Single(p => p.Id == packId);
        var booking = NewBooking(await GuestAsync(api), BookingPaymentMethod.Stripe,
            Line(tourA, departuresA[0]), Line(tourB, departuresB[0]), Line(tourC, departuresC[0])) with
        {
            PackIncentiveId = packId,
            PackIncentiveTierId = pack.Tiers[0].Id,
        };
        var id = await api.Send(new SaveBooking(booking), Ct);
        var cancelled = (await api.Send(new GetBookingEditor(id), Ct)).Lines.Single(l => l.TourId == tourC);
        await api.Send(new CancelBookingItem(cancelled.Key, 0), Ct);

        // As legacy: the cancelled tour still counts toward the pack (so the booking still saves) and keeps its share.
        var editor = await api.Send(new GetBookingEditor(id), Ct);
        editor.AdditionalNotes = "Changed";
        await api.Send(new SaveBooking(editor), Ct);

        var saved = await api.Send(new GetBookingEditor(id), Ct);
        saved.Lines.ShouldAllBe(l => l.Amount == 20);
        saved.Lines.Single(l => l.TourId == tourC).Cancelled.ShouldBeTrue();
        saved.SavedTotal.ShouldBe(60);
        saved.PackIncentiveId.ShouldBe(packId);
    }

    [Fact]
    public async Task A_booked_packages_snapshot_only_changes_when_the_package_is_edited_again()
    {
        var api = await app.AdminApiAsync();
        var (tourA, departuresA) = await TourAsync(api, 30, 10, Day(23));
        var (tourB, departuresB) = await TourAsync(api, 35, 10, Day(24));
        var adult = Guid.NewGuid();
        var name = $"Combo {Guid.NewGuid():N}";
        var packageId = await api.Send(new SavePackage(null, new PackageChanges
        {
            Details = new PackageInput { Name = name, CommissionAmount = 5 },
            Tiers = [new() { Key = adult, Title = "Adult" }],
            Items =
            [
                new() { TourId = tourA, Quantity = 1, Prices = new() { [adult] = 15 } },
                new() { TourId = tourB, Quantity = 1, Prices = new() { [adult] = 12 } },
            ],
        }, null, false), Ct);
        var package = (await api.Send(new GetBookingCatalog(), Ct)).Packages.Single(p => p.Id == packageId);
        var key = Guid.NewGuid();
        var lines = new[] { Line(tourA, departuresA[0]), Line(tourB, departuresB[0]) };
        foreach (var line in lines)
            line.PackageKey = key;
        var id = await api.Send(new SaveBooking(NewBooking(await GuestAsync(api), BookingPaymentMethod.Stripe, lines) with
        {
            Packages = [new BookingPackageLine { Key = key, PackageId = packageId, PackageName = package.Name, TierId = package.Tiers.Single().Id, Edited = true }],
        }), Ct);

        // The package changes in the catalog afterwards.
        var definition = await api.Send(new GetPackageEditor(packageId), Ct);
        await api.Send(new SavePackage(packageId, new PackageChanges
        {
            Details = definition.Details with { Name = name + " renamed", CommissionAmount = 9, CommissionIgnoresMinimumPax = true },
            Tiers = [.. definition.Tiers],
            Items = [.. definition.Items.Select(i => new PackageItemEdit { Id = i.Id, TourId = i.TourId, Quantity = i.Quantity, Prices = i.Prices.ToDictionary(p => p.Key, p => p.Value + 10) })],
        }, null, false), Ct);

        async Task<(string Name, decimal Price, decimal? Commission, bool? IgnoresMinimumPax)> SnapshotAsync()
        {
            await using var scope = TestBookings.TenantScope(app);
            var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>().BookingPackages.AsNoTracking().SingleAsync(p => p.BookingId == id, Ct);
            return (saved.PackageName, saved.PackagePrice, saved.CommissionAmount, saved.CommissionIgnoresMinimumPax);
        }

        // Saving the booking for something else leaves the booked snapshot (and the tours' prices) alone, as legacy.
        var editor = await api.Send(new GetBookingEditor(id), Ct);
        editor.AdditionalNotes = "Changed";
        await api.Send(new SaveBooking(editor), Ct);
        (await SnapshotAsync()).ShouldBe((name, 27m, 5m, (bool?)false));
        (await api.Send(new GetBookingEditor(id), Ct)).SavedTotal.ShouldBe(27);

        // Editing the package again (the package dialog) re-reads it.
        editor = await api.Send(new GetBookingEditor(id), Ct);
        editor.Packages.Single().Edited = true;
        await api.Send(new SaveBooking(editor), Ct);
        (await SnapshotAsync()).ShouldBe((name + " renamed", 47m, 9m, (bool?)true));
    }

    [Fact]
    public async Task Changing_the_lead_guest_to_one_without_an_email_stores_their_placeholder_as_legacy()
    {
        var api = await app.AdminApiAsync();
        var (tourId, departures) = await TourAsync(api, 25, 10, Day(25));
        var id = await api.Send(new SaveBooking(NewBooking(await GuestAsync(api), BookingPaymentMethod.Stripe, Line(tourId, departures[0]))), Ct);
        var noEmail = await api.Send(new CreateGuest(new UserProfile { FirstName = "No", LastName = "Email" }), Ct);

        var editor = await api.Send(new GetBookingEditor(id), Ct);
        editor.GuestId = noEmail;
        await api.Send(new SaveBooking(editor), Ct);

        await using var scope = TestBookings.TenantScope(app);
        var booking = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.AsNoTracking().SingleAsync(b => b.Id == id, Ct);
        (booking.GuestId, booking.GuestName).ShouldBe((noEmail, "No Email"));
        booking.GuestEmail.ShouldEndWith("@travaloudguest.com"); // legacy's: the guest account's own placeholder
        // It's never emailed or sent to Stripe: a payment link is refused as for a guest with no email.
        (await Should.ThrowAsync<BusinessRuleException>(() => api.Send(new SendPaymentEmail(id), Ct).AsTask())).Message.ShouldContain("doesn't have an email");
    }

    [Fact]
    public async Task Changing_a_paid_bookings_total_needs_a_decision()
    {
        var api = await app.AdminApiAsync();
        var (tourId, departures) = await TourAsync(api, 25, 10, Day(19));
        var id = await api.Send(new SaveBooking(NewBooking(await GuestAsync(api), BookingPaymentMethod.Cash, Line(tourId, departures[0]))), Ct);

        var editor = await api.Send(new GetBookingEditor(id), Ct);
        editor.Lines[0].GuestIds.Add(await GuestAsync(api));
        (await Should.ThrowAsync<BusinessRuleException>(() => api.Send(new SaveBooking(editor), Ct).AsTask())).Message.ShouldContain("Choose how");

        await api.Send(new SaveBooking(editor, PaidTotalChange.ChargeDifference), Ct);
        var row = await RowAsync(api, id);
        (row.Status, row.Outstanding, row.TotalAmount, row.IsPaid).ShouldBe((BookingState.PartiallyPaid, 25m, 50m, false));
    }

    [Fact]
    public void Pack_prices_round_each_tour_but_total_the_exact_shares_as_legacy()
    {
        var pack = new PackOption { Id = Guid.NewGuid(), Name = "Pack", MinimumTours = 3, TourIds = [], Tiers = [new(Guid.NewGuid(), "Adult", 100, 0)] };
        var tours = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        pack = pack with { TourIds = [.. tours] };
        var lines = tours.Select(t => new BookingLine { Key = Guid.NewGuid(), IsNew = true, TourId = t, DeparturePrice = 40 }).ToList();
        lines[0].GuestIds.Add("extra-guest");

        BookingPricing.Apply(lines, pack, pack.Tiers[0].Id, [], []);

        // Legacy stored 100/3 to the cent on each tour but totalled the exact shares: 4 seats × 33.333… = 133.33 (not 4 × 33.33).
        lines.ShouldAllBe(l => l.Amount == 33.33m);
        BookingPricing.Total(lines).ShouldBe(133.33m);
        new BookingEditor { Lines = lines }.Total.ShouldBe(133.33m);

        // $200 of three: 66.67 each (SQL Server rounds half away from zero) and exactly $200.00.
        BookingPricing.Apply(lines, pack with { Tiers = [new(pack.Tiers[0].Id, "Adult", 200, 0)] }, pack.Tiers[0].Id, [], []);
        lines.ShouldAllBe(l => l.Amount == 66.67m);
        BookingPricing.Total(lines).ShouldBe(266.67m);
        BookingPricing.PackPrice(100.01m, 2).ShouldBe(50.01m); // a midpoint rounds up, as SQL Server stored it

        // Without the pack the exact share is gone.
        BookingPricing.Apply(lines, null, null, [], []);
        lines.ShouldAllBe(l => l.PackShare == null && l.Amount == 40);
        BookingPricing.Total(lines).ShouldBe(160);
    }

    [Fact]
    public async Task A_pack_booking_stores_each_share_to_the_cent_and_totals_the_exact_shares()
    {
        var api = await app.AdminApiAsync();
        var (tourA, departuresA) = await TourAsync(api, 30, 10, Day(23));
        var (tourB, departuresB) = await TourAsync(api, 35, 10, Day(24));
        var (tourC, departuresC) = await TourAsync(api, 40, 10, Day(25));
        var packId = await api.Send(new SavePackIncentive(null, new PackIncentiveInput { Name = $"Pack {Guid.NewGuid():N}", MinimumProductCount = 3, CommissionAmount = 5 },
            [tourA, tourB, tourC], [new() { Title = "Adult", Price = 100 }]), Ct);
        var pack = (await api.Send(new GetBookingCatalog(), Ct)).Packs.Single(p => p.Id == packId);
        var booking = NewBooking(await GuestAsync(api), BookingPaymentMethod.Cash,
            Line(tourA, departuresA[0], await GuestAsync(api)), Line(tourB, departuresB[0]), Line(tourC, departuresC[0])) with
        {
            PackIncentiveId = packId,
            PackIncentiveTierId = pack.Tiers[0].Id,
        };
        var id = await api.Send(new SaveBooking(booking), Ct);

        var saved = await api.Send(new GetBookingEditor(id), Ct);
        saved.Lines.ShouldAllBe(l => l.Amount == 33.33m);
        saved.SavedTotal.ShouldBe(133.33m);

        // The paid booking re-saves without its total "changing" by a cent.
        BookingPricing.Apply(saved.Lines, pack, saved.PackIncentiveTierId, [], []);
        saved.Total.ShouldBe(saved.SavedTotal);
        saved.AdditionalNotes = "Changed";
        await api.Send(new SaveBooking(saved), Ct);
        (await api.Send(new GetBookingEditor(id), Ct)).SavedTotal.ShouldBe(133.33m);
    }

    /// <summary>A new staff member who isn't an administrator (Tour Manager: manages tour bookings).</summary>
    private async Task<IApi> StaffApiAsync()
    {
        var email = $"staff-{Guid.NewGuid():N}@tests.invalid";
        await using (var scope = TestBookings.TenantScope(app))
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, FirstName = "Tess", LastName = "Staff", FullName = "Tess Staff", IsActive = true, EmailConfirmed = true };
            (await users.CreateAsync(user, TestApp.Password)).Succeeded.ShouldBeTrue();
            await users.AddToRoleAsync(user, FuseRoles.TourManager);
        }
        return new Fuse.Web.Client.Api.HttpApi(await app.SignedInAsync(email, staffOnly: true));
    }

    [Fact]
    public async Task On_a_paid_booking_staff_move_departures_administrators_also_change_tours_and_guests_and_nobody_adds_or_removes()
    {
        var admin = await app.AdminApiAsync();
        var staff = await StaffApiAsync();
        var (tourId, departures) = await TourAsync(admin, 25, 10, Day(26), Day(27));
        var (other, otherDepartures) = await TourAsync(admin, 25, 10, Day(26));
        var id = await admin.Send(new SaveBooking(NewBooking(await GuestAsync(admin), BookingPaymentMethod.Cash,
            Line(tourId, departures[0]), Line(other, otherDepartures[0]))), Ct);
        (await admin.Send(new GetBookingEditor(id), Ct)).IsPaid.ShouldBeTrue();

        // Staff: another departure is fine (legacy Tour Date and Pick Up stayed enabled)...
        var editor = await staff.Send(new GetBookingEditor(id), Ct);
        editor.Lines.Single(l => l.TourId == tourId).TourDateId = departures[1].Id;
        await staff.Send(new SaveBooking(editor), Ct);
        (await SpacesAsync(departures[1].Id)).ShouldBe(9);

        // ...but not another tour or other guests.
        editor = await staff.Send(new GetBookingEditor(id), Ct);
        editor.Lines.Single(l => l.TourId == tourId).GuestIds.Add(await GuestAsync(admin));
        (await Should.ThrowAsync<BusinessRuleException>(() => staff.Send(new SaveBooking(editor, PaidTotalChange.ChargeDifference), Ct).AsTask())).Message.ShouldContain("administrator");
        editor = await staff.Send(new GetBookingEditor(id), Ct);
        var line = editor.Lines.Single(l => l.TourId == other);
        (line.TourId, line.TourDateId) = (tourId, departures[0].Id);
        (await Should.ThrowAsync<BusinessRuleException>(() => staff.Send(new SaveBooking(editor), Ct).AsTask())).Message.ShouldContain("administrator");

        // Nobody adds or removes tours on a paid booking, administrators included.
        foreach (var api in new[] { staff, admin })
        {
            editor = await api.Send(new GetBookingEditor(id), Ct);
            editor.Lines.Add(Line(other, otherDepartures[0]));
            (await Should.ThrowAsync<BusinessRuleException>(() => api.Send(new SaveBooking(editor, PaidTotalChange.ChargeDifference), Ct).AsTask())).Message.ShouldContain("can't be added");
            editor = await api.Send(new GetBookingEditor(id), Ct);
            editor.Lines.RemoveAll(l => l.TourId == other);
            (await Should.ThrowAsync<BusinessRuleException>(() => api.Send(new SaveBooking(editor, PaidTotalChange.KeepAmountPaid), Ct).AsTask())).Message.ShouldContain("can't be removed");
        }

        // An administrator may change the guests (and the tour).
        editor = await admin.Send(new GetBookingEditor(id), Ct);
        editor.Lines.Single(l => l.TourId == tourId).GuestIds.Add(await GuestAsync(admin));
        await admin.Send(new SaveBooking(editor, PaidTotalChange.ChargeDifference), Ct);
        (await SpacesAsync(departures[1].Id)).ShouldBe(8);
    }

    [Fact]
    public void The_wizard_gates_a_paid_bookings_tours_as_legacy()
    {
        BookingDraft Draft(bool paid, bool refunded, bool admin) =>
            new(new BookingEditor { Id = Guid.NewGuid(), IsPaid = paid, Refunded = refunded }, new BookingCatalog(), new BookingAbilities { IsAdmin = admin });

        var open = Draft(paid: false, refunded: false, admin: false);
        (open.CanChangeTours, open.CanEditTours, open.CanChangeTourAndGuests, open.CanChangeDeparture).ShouldBe((true, true, true, true));
        var paidStaff = Draft(paid: true, refunded: false, admin: false);
        (paidStaff.CanChangeTours, paidStaff.CanEditTours, paidStaff.CanChangeTourAndGuests, paidStaff.CanChangeDeparture).ShouldBe((false, true, false, true));
        var paidAdmin = Draft(paid: true, refunded: false, admin: true);
        (paidAdmin.CanChangeTours, paidAdmin.CanEditTours, paidAdmin.CanChangeTourAndGuests, paidAdmin.CanChangeDeparture).ShouldBe((false, true, true, true));
        var refundedAdmin = Draft(paid: false, refunded: true, admin: true);
        (refundedAdmin.CanChangeTours, refundedAdmin.CanChangeTourAndGuests, refundedAdmin.CanChangeDeparture).ShouldBe((false, true, false));
    }
}
