using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Application.Common.Cloudbeds;
using Fuse.Application.Features.Dashboard;
using Fuse.Contracts.Api;
using Fuse.Contracts.Common;
using Fuse.Contracts.Dashboard;
using Fuse.Domain.Catalog.Bookings;
using Fuse.Domain.Identity;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Authorization;

namespace Fuse.Web.Tests;

/// <summary>The dashboard's Sell a Tour Today panel.</summary>
public sealed class TourSellingTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeCloudbeds Cloudbeds => (FakeCloudbeds)app.Services.GetRequiredService<ICloudbedsClient>();

    private static DateOnly Today => DateOnly.FromDateTime(TestBookings.BusinessNow);

    private int Calls(string propertyId) => Cloudbeds.ReservationListCalls.GetValueOrDefault(propertyId);

    private static DateTime At(DateOnly day, int hour = 9) => day.ToDateTime(new TimeOnly(hour, 0));

    private async Task HostelAsync(string name, string cloudbedsId)
    {
        await using var scope = TestBookings.TenantScope(app);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync("""
            DECLARE @now datetime2 = SYSUTCDATETIME(), @empty uniqueidentifier = '00000000-0000-0000-0000-000000000000';
            INSERT INTO Catalog.Properties (Id, Name, Description, AddressLine1, TelephoneNumber, CloudbedsPropertyId, CloudbedsApiKey, TenantId, CreatedBy, CreatedOn, LastModifiedBy)
            VALUES (NEWID(), {0}, 'A hostel', '1 Test Street', '000', {1}, 'key', 'fuse', @empty, @now, @empty);
            """, new object[] { name, cloudbedsId }, Ct);
    }

    /// <summary>A customer here linked to Cloudbeds guest <paramref name="cloudbedsId"/>; their id.</summary>
    private async Task<string> GuestAsync(string cloudbedsId)
    {
        var email = $"{cloudbedsId}@tests.invalid";
        await using var scope = TestBookings.TenantScope(app);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, FirstName = "Stay", LastName = cloudbedsId, CloudbedsGuestId = cloudbedsId, IsActive = true };
        (await users.CreateAsync(user)).Succeeded.ShouldBeTrue();
        await users.AddToRoleAsync(user, FuseRoles.Guest);
        return user.Id;
    }

    /// <summary>A tour booked by <paramref name="leadId"/> at <paramref name="start"/>, priced per person, with others on it.</summary>
    private async Task BookAsync((DefaultIdType TourId, DefaultIdType TourDateId) tour, string leadId, DateTime start, decimal price,
        string[]? guestIds = null, bool cancelled = false, bool refunded = false)
    {
        await using var scope = TestBookings.TenantScope(app);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var others = guestIds ?? [];
        var item = new BookingItem(start, start.AddHours(2), price, null, null, tour.TourId, tour.TourDateId, null, null, "Test Hostel", true, null, DefaultIdType.Empty)
        {
            Guests = [.. others.Select(id => new BookingItemGuest(Guid.Parse(id)))],
        };
        var booking = new Booking("Attach booking", price * (1 + others.Length), "USD", 1, true, TestBookings.BusinessNow, leadId, null, true, "Lead",
            null, null, "Website", refunded, 0)
        {
            Items = [item],
            Payments = [],
        };
        if (cancelled)
            booking.Cancel();
        db.Bookings.Add(booking);
        await db.SaveChangesAsync(Ct);
    }

    private static CloudbedsReservation Reservation(string id, string guestId, string status, DateOnly from, DateOnly to, string source = "Direct") =>
        new(id, status, from, to, 0, source, [new("6 Bed Dorm", $"R1 - Bed {id}")], guestId, $"Guest {id}");

    private static DateOnly Monday(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    [Fact]
    public async Task Sell_a_tour_today_lists_todays_arrivals_and_in_house_guests_with_no_tour_booked()
    {
        var run = Guid.NewGuid().ToString("N")[..8];
        var (hostel, other) = ($"Sell Hostel {run}", $"Sell Other {run}");
        await HostelAsync(hostel, $"cb-sell-{run}");
        await HostelAsync(other, $"cb-sello-{run}");
        var booked = await GuestAsync($"st-booked-{run}");
        var free = await GuestAsync($"st-free-{run}");
        var api = await app.AdminApiAsync();
        var tour = await TestBookings.TourWithDepartureAsync(app, api, At(Today.AddDays(1)), Ct);
        await BookAsync(tour, booked, At(Today.AddDays(1)), 25);

        Cloudbeds.ByStatus[$"cb-sell-{run}"] = new()
        {
            ["checked_in"] =
            [
                Reservation("S1", $"st-booked-{run}", "checked_in", Today.AddDays(-1), Today.AddDays(3)), // has a tour tomorrow
                Reservation("S2", $"st-free-{run}", "checked_in", Today.AddDays(-2), Today.AddDays(4)),
                Reservation("S3", $"st-new-{run}", "checked_in", Today.AddDays(-1), Today.AddDays(1)), // not imported yet
            ],
            ["confirmed"] =
            [
                Reservation("S4", $"st-arrive-{run}", "confirmed", Today, Today.AddDays(3)),
                Reservation("S5", $"st-tomorrow-{run}", "confirmed", Today.AddDays(1), Today.AddDays(2)), // tomorrow: the report only
            ],
        };
        Cloudbeds.ByStatus[$"cb-sello-{run}"] = new()
        {
            ["checked_in"] = [Reservation("T1", $"st-other-{run}", "checked_in", Today.AddDays(-1), Today.AddDays(2))],
        };
        app.Services.GetRequiredService<IMemoryCache>().Remove(GuestsWithoutTours.CacheKey);

        var summary = await api.Send(new GetSellATourSummary(hostel), Ct);
        (summary.ArrivingToday, summary.InHouse).ShouldBe((1, 2));
        summary.Hostels.ShouldContain(hostel);
        summary.Hostels.ShouldContain(other);
        summary.Unreachable.ShouldNotContain(hostel);

        Task<PagedResult<SellATourRow>> Search(SearchSellATourToday query) => api.Send(query with { Hostel = query.Hostel ?? hostel }, Ct).AsTask();

        // By default in house leaving soonest first, then today's arrivals.
        var page = await Search(new SearchSellATourToday());
        page.TotalCount.ShouldBe(3);
        page.Items.Select(g => (g.Name, g.InHouse, g.NightsLeft)).ShouldBe([("Guest S3", true, 1), ("Guest S2", true, 4), ("Guest S4", false, 3)]);
        page.Items.Single(g => g.Name == "Guest S2").GuestId.ShouldBe(free);
        page.Items.Single(g => g.Name == "Guest S3").GuestId.ShouldBeNull();
        page.Items.ShouldAllBe(g => g.Hostel == hostel);
        page.Items.Single(g => g.Name == "Guest S4").CheckOut.ShouldBe(Today.AddDays(3));

        // Pages.
        var second = await Search(new SearchSellATourToday { PageNumber = 2, PageSize = 2 });
        (second.TotalCount, second.Items.ShouldHaveSingleItem().Name).ShouldBe((3, "Guest S4"));

        // Sorted by a column.
        (await Search(new SearchSellATourToday { OrderBy = ["NightsLeft desc"] })).Items.Select(g => g.Name).ShouldBe(["Guest S2", "Guest S4", "Guest S3"]);
        (await Search(new SearchSellATourToday { OrderBy = ["InHouse"] })).Items.First().Name.ShouldBe("Guest S4");

        // Searched by name, or by room (not a column, but on the guest's reservation).
        (await Search(new SearchSellATourToday { Keyword = "s4" })).Items.ShouldHaveSingleItem().Name.ShouldBe("Guest S4");
        (await Search(new SearchSellATourToday { Keyword = "Bed S2" })).Items.ShouldHaveSingleItem().Name.ShouldBe("Guest S2");

        // One hostel's, or every hostel's.
        (await Search(new SearchSellATourToday { Hostel = other })).Items.ShouldHaveSingleItem().Hostel.ShouldBe(other);
        var all = await api.Send(new SearchSellATourToday { PageSize = 500 }, Ct);
        all.Items.Where(g => g.Hostel == hostel || g.Hostel == other).Select(g => g.Name).ShouldBe(["Guest S3", "Guest S2", "Guest T1", "Guest S4"], ignoreOrder: true);

        Cloudbeds.ByStatus.TryRemove($"cb-sell-{run}", out _);
        Cloudbeds.ByStatus.TryRemove($"cb-sello-{run}", out _);
    }
}
