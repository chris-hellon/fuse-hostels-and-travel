using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Application.Common.Cloudbeds;
using Fuse.Contracts.Bookings;
using Fuse.Contracts.Dashboard;
using Fuse.Contracts.Identity;
using Fuse.Domain.Identity;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Authorization;

namespace Fuse.Web.Tests;

/// <summary>Cloudbeds stays for tour selling: who's in house with no tours booked, and a guest's current stay.</summary>
public sealed class HostelStayTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeCloudbeds Cloudbeds => (FakeCloudbeds)app.Services.GetRequiredService<ICloudbedsClient>();

    private static DateOnly Today => DateOnly.FromDateTime(TestBookings.BusinessNow);

    /// <summary>The dashboard keeps Cloudbeds' answer for a couple of minutes: ask afresh.</summary>
    private void Fresh() => app.Services.GetRequiredService<IMemoryCache>().Remove(Application.Features.Dashboard.GuestsWithoutTours.CacheKey);

    private async Task HostelAsync(string name, string cloudbedsId)
    {
        await using var scope = TestBookings.TenantScope(app);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync("""
            DECLARE @now datetime2 = SYSUTCDATETIME(), @empty uniqueidentifier = '00000000-0000-0000-0000-000000000000';
            INSERT INTO Catalog.Properties (Id, Name, Description, AddressLine1, TelephoneNumber, CloudbedsPropertyId, CloudbedsApiKey, TenantId, CreatedBy, CreatedOn, LastModifiedBy)
            VALUES (NEWID(), {0}, 'A hostel', '1 Test Street', '000', {1}, 'key', 'fuse', @empty, @now, @empty);
            """, new object[] { name, cloudbedsId }, Ct);
    }

    /// <summary>A guest here linked to Cloudbeds guest <paramref name="cloudbedsId"/>; their email and id.</summary>
    private async Task<(string Email, string Id)> GuestAsync(string cloudbedsId)
    {
        var email = $"{cloudbedsId}@tests.invalid";
        await using var scope = TestBookings.TenantScope(app);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, FirstName = "Stay", LastName = cloudbedsId, CloudbedsGuestId = cloudbedsId, IsActive = true };
        (await users.CreateAsync(user)).Succeeded.ShouldBeTrue();
        await users.AddToRoleAsync(user, FuseRoles.Guest);
        return (email, user.Id);
    }

    private static CloudbedsReservation Reservation(string id, string guestId, string name, string status, DateOnly from, DateOnly to) =>
        new(id, status, from, to, 0, "Direct", [new("6 Bed Dorm", "R1 - Bed 2")], guestId, name);

    [Fact]
    public async Task The_dashboard_lists_guests_staying_or_arriving_with_no_tours_during_their_stay()
    {
        var run = Guid.NewGuid().ToString("N")[..8];
        var hostel = $"Stay Hostel {run}";
        await HostelAsync(hostel, $"cb-{run}");
        var (bookedEmail, _) = await GuestAsync($"booked-{run}");
        var (_, freeId) = await GuestAsync($"free-{run}");

        // "booked" has a tour tomorrow, during their stay.
        var api = await app.AdminApiAsync();
        var tomorrow = TestBookings.BusinessNow.Date.AddDays(1).AddHours(9);
        var (tourId, departureId) = await TestBookings.TourWithDepartureAsync(app, api, tomorrow, Ct);
        await TestBookings.BookAsync(app, tourId, departureId, tomorrow, TestBookings.BusinessNow, Ct, guestEmail: bookedEmail);

        Cloudbeds.ByStatus[$"cb-{run}"] = new()
        {
            ["checked_in"] =
            [
                Reservation("R1", $"booked-{run}", "Bea Booked", "checked_in", Today.AddDays(-1), Today.AddDays(3)),
                Reservation("R2", $"free-{run}", "Fay Free", "checked_in", Today.AddDays(-2), Today.AddDays(2)),
                Reservation("R3", $"new-{run}", "Nia New", "checked_in", Today, Today.AddDays(1)), // not imported yet
            ],
            ["confirmed"] =
            [
                Reservation("R4", $"arrive-{run}", "Ari Arriving", "confirmed", Today.AddDays(1), Today.AddDays(4)),
                Reservation("R5", $"later-{run}", "Lou Later", "confirmed", Today.AddDays(5), Today.AddDays(7)), // not by tomorrow
            ],
        };

        Fresh();
        var inHouse = await api.Send(new SearchGuestsWithoutTours { Hostel = hostel, PageSize = 50 }, Ct);
        var arriving = await api.Send(new SearchGuestsWithoutTours { Hostel = hostel, Arriving = true, PageSize = 50 }, Ct);

        inHouse.Items.Select(g => g.Name).ShouldBe(["Nia New", "Fay Free"]); // leaving soonest first; Bea has a tour
        inHouse.Items.Single(g => g.Name == "Fay Free").GuestId.ShouldBe(freeId);
        inHouse.Items.Single(g => g.Name == "Nia New").GuestId.ShouldBeNull();
        inHouse.Items[0].ReservationUrl.ShouldBe($"https://hotels.cloudbeds.com/connect/cb-{run}#/reservations/rR3");
        arriving.Items.Select(g => g.Name).ShouldBe(["Ari Arriving"]); // Lou arrives after tomorrow
        (await api.Send(new SearchGuestsWithoutTours { Hostel = hostel, Keyword = "fay" }, Ct)).Items.ShouldHaveSingleItem().Name.ShouldBe("Fay Free");
        (await api.Send(new GetGuestsWithoutToursHostels(), Ct)).Hostels.ShouldContain(hostel);
        Cloudbeds.ByStatus.TryRemove($"cb-{run}", out _);
    }

    [Fact]
    public async Task Hostel_bookings_list_each_hostels_reservations_with_the_customer_and_their_tours_during_the_stay()
    {
        var run = Guid.NewGuid().ToString("N")[..8];
        var hostel = $"Bookings Hostel {run}";
        await HostelAsync(hostel, $"cb-{run}");
        var (bookedEmail, bookedId) = await GuestAsync($"hb-booked-{run}");
        var api = await app.AdminApiAsync();
        var tomorrow = TestBookings.BusinessNow.Date.AddDays(1).AddHours(9);
        var (tourId, departureId) = await TestBookings.TourWithDepartureAsync(app, api, tomorrow, Ct);
        await TestBookings.BookAsync(app, tourId, departureId, tomorrow, TestBookings.BusinessNow, Ct, guestEmail: bookedEmail);

        Cloudbeds.ByStatus[$"cb-{run}"] = new()
        {
            ["checked_in"] =
            [
                Reservation("H1", $"hb-booked-{run}", "Bea Booked", "checked_in", Today.AddDays(-1), Today.AddDays(3)),
                Reservation("H2", $"hb-new-{run}", "Nia New", "checked_in", Today, Today.AddDays(1)), // not imported yet
            ],
            ["confirmed"] = [Reservation("H3", $"hb-arrive-{run}", "Ari Arriving", "confirmed", Today.AddDays(2), Today.AddDays(4))],
            ["canceled"] = [Reservation("H4", $"hb-gone-{run}", "Cal Cancelled", "canceled", Today.AddDays(-3), Today.AddDays(-1))],
        };
        var cache = app.Services.GetRequiredService<IMemoryCache>();
        cache.Remove(Application.Features.Bookings.HostelBookings.CacheKey(HostelBookingsView.InHouse, null, null));
        cache.Remove(Application.Features.Bookings.HostelBookings.CacheKey(HostelBookingsView.Arriving, Today, Today.AddDays(Application.Features.Bookings.HostelBookings.ArrivingDays)));

        var inHouse = (await api.Send(new SearchHostelBookings { Hostel = hostel, PageSize = 50 }, Ct)).Items;
        var arriving = (await api.Send(new SearchHostelBookings { View = HostelBookingsView.Arriving, Hostel = hostel, PageSize = 50 }, Ct)).Items;
        var all = (await api.Send(new SearchHostelBookings
        {
            View = HostelBookingsView.All, Hostel = hostel, CheckInFrom = Today.AddDays(-5), CheckInTo = Today.AddDays(5), PageSize = 50,
        }, Ct)).Items;

        inHouse.Select(r => r.ReservationId).ShouldBe(["H2", "H1"]); // leaving soonest first
        var bea = inHouse.Single(r => r.ReservationId == "H1");
        (bea.GuestId, bea.GuestEmail, bea.Tours, bea.Nights, bea.Status).ShouldBe((bookedId, bookedEmail, 1, 4, "In house"));
        bea.Url.ShouldBe($"https://hotels.cloudbeds.com/connect/cb-{run}#/reservations/rH1");
        inHouse.Single(r => r.ReservationId == "H2").GuestId.ShouldBeNull();
        arriving.Select(r => r.ReservationId).ShouldBe(["H3"]);
        all.Select(r => r.ReservationId).ShouldBe(["H4", "H1", "H2", "H3"]); // any status, by check-in
        all.Single(r => r.ReservationId == "H4").Status.ShouldBe("Cancelled");
        (await api.Send(new SearchHostelBookings { Hostel = hostel, Keyword = "nia" }, Ct)).Items.ShouldHaveSingleItem().ReservationId.ShouldBe("H2");
        (await api.Send(new GetHostelBookingHostels(), Ct)).Hostels.ShouldContain(hostel);
        Cloudbeds.ByStatus.TryRemove($"cb-{run}", out _);
    }

    [Fact]
    public async Task A_hostel_whose_Cloudbeds_cant_be_reached_says_so()
    {
        await HostelAsync($"Down Hostel {Guid.NewGuid():N}", "cb-down");

        Fresh();
        var result = await (await app.AdminApiAsync()).Send(new GetGuestsWithoutToursHostels(), Ct);

        result.Unreachable.ShouldContain(h => h.StartsWith("Down Hostel"));
    }

    [Fact]
    public async Task With_no_stay_now_or_next_a_guests_most_recent_reservation_is_shown()
    {
        var run = Guid.NewGuid().ToString("N")[..8];
        await HostelAsync($"Past Hostel {run}", $"cb-{run}");
        var (_, id) = await GuestAsync($"past-{run}");
        Cloudbeds.Reservations[$"cb-{run}"] = new()
        {
            [$"past-{run}"] =
            [
                Reservation("OLDER", $"past-{run}", "Past", "checked_out", Today.AddDays(-40), Today.AddDays(-35)),
                Reservation("RECENT", $"past-{run}", "Past", "checked_out", Today.AddDays(-7), Today.AddDays(-2)),
                Reservation("CANCELLED", $"past-{run}", "Past", "canceled", Today.AddDays(-1), Today.AddDays(1)),
            ],
        };

        var result = await (await app.AdminApiAsync()).Send(new GetGuestCurrentStay(id), Ct);

        // Not a current stay (pick-ups and late-departure warnings ignore it), but shown as their most recent one.
        (result.Stay, result.InHouse, result.Last?.ReservationId, result.Last?.Status).ShouldBe((null, false, "RECENT", "Checked out"));
        Cloudbeds.Reservations.TryRemove($"cb-{run}", out _);
    }

    [Fact]
    public async Task A_guests_current_stay_is_the_one_theyre_in_or_else_their_next()
    {
        var run = Guid.NewGuid().ToString("N")[..8];
        await HostelAsync($"Current Hostel {run}", $"cb-{run}");
        var (_, id) = await GuestAsync($"cur-{run}");
        var api = await app.AdminApiAsync();

        Cloudbeds.Reservations[$"cb-{run}"] = new()
        {
            [$"cur-{run}"] =
            [
                Reservation("OLD", $"cur-{run}", "Cur", "checked_out", Today.AddDays(-20), Today.AddDays(-15)),
                Reservation("NEXT", $"cur-{run}", "Cur", "confirmed", Today.AddDays(10), Today.AddDays(12)),
            ],
        };
        var next = await api.Send(new GetGuestCurrentStay(id), Ct);
        (next.Stay?.ReservationId, next.InHouse).ShouldBe(("NEXT", false));

        Cloudbeds.Reservations[$"cb-{run}"][$"cur-{run}"] =
            [.. Cloudbeds.Reservations[$"cb-{run}"][$"cur-{run}"], Reservation("NOW", $"cur-{run}", "Cur", "checked_in", Today.AddDays(-1), Today.AddDays(2))];
        var now = await api.Send(new GetGuestCurrentStay(id), Ct);
        (now.Stay?.ReservationId, now.InHouse, now.Stay?.Hostel).ShouldBe(("NOW", true, $"Current Hostel {run}"));
        Cloudbeds.Reservations.TryRemove($"cb-{run}", out _);
    }
}
