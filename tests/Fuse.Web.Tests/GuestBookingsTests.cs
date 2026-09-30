using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Application.Common.Cloudbeds;
using Fuse.Contracts.Dashboard;
using Fuse.Contracts.Identity;
using Fuse.Domain.Identity;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Authorization;

namespace Fuse.Web.Tests;

/// <summary>A guest's bookings on their profile: tour bookings here, hostel stays from Cloudbeds.</summary>
public sealed class GuestBookingsTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeCloudbeds Cloudbeds => (FakeCloudbeds)app.Services.GetRequiredService<ICloudbedsClient>();

    private async Task<string> NewGuestAsync(string kind)
    {
        var email = $"{kind}-{Guid.NewGuid():N}@tests.invalid";
        await (await app.AdminApiAsync()).Send(new CreateGuest(new UserProfile { FirstName = "Tour", LastName = kind, Email = email }), Ct);
        return email;
    }

    private async Task<string> UserIdAsync(string email)
    {
        await using var scope = TestBookings.TenantScope(app);
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id;
    }

    [Fact]
    public async Task A_guests_tour_bookings_show_whether_they_booked_or_are_a_guest_on_it()
    {
        var api = await app.AdminApiAsync();
        var start = TestBookings.BusinessNow.Date.AddDays(5).AddHours(9);
        var (tourId, departureId) = await TestBookings.TourWithDepartureAsync(app, api, start, Ct);
        // Their own guests (the shared test guest and admin have too many bookings to find one among the newest).
        var bookerEmail = await NewGuestAsync("booker");
        var onTourEmail = await NewGuestAsync("ontour");
        var reference = await TestBookings.BookAsync(app, tourId, departureId, start, TestBookings.BusinessNow, Ct, guestEmail: bookerEmail, onTourEmail: onTourEmail);
        string tourName;
        await using (var scope = TestBookings.TenantScope(app))
            tourName = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tours.Where(t => t.Id == tourId).Select(t => t.Name).SingleAsync(Ct);

        var booker = (await api.Send(new ListGuestTourBookings(await UserIdAsync(bookerEmail)), Ct)).ShouldHaveSingleItem();
        var onIt = (await api.Send(new ListGuestTourBookings(await UserIdAsync(onTourEmail)), Ct)).ShouldHaveSingleItem();
        (booker.Reference, onIt.Reference).ShouldBe((reference, reference));

        (booker.LeadBooker, booker.Status, booker.TotalAmount).ShouldBe((true, BookingState.Paid, 50m));
        booker.Tours.ShouldHaveSingleItem().ShouldBe(new GuestTourBookingItem(tourName, start, false));
        onIt.LeadBooker.ShouldBeFalse();
    }

    [Fact]
    public async Task Hostel_stays_come_from_every_hostels_Cloudbeds_and_say_which_couldnt_be_asked()
    {
        var run = Guid.NewGuid().ToString("N")[..10];
        var guestId = $"stay-{run}";
        string id;
        await using (var scope = TestBookings.TenantScope(app))
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = $"{guestId}@tests.invalid", Email = $"{guestId}@tests.invalid", FirstName = "Stella", LastName = "Stay", CloudbedsGuestId = guestId, IsActive = true };
            (await users.CreateAsync(user)).Succeeded.ShouldBeTrue();
            await users.AddToRoleAsync(user, FuseRoles.Guest);
            id = user.Id;
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync("""
                DECLARE @now datetime2 = SYSUTCDATETIME(), @empty uniqueidentifier = '00000000-0000-0000-0000-000000000000';
                INSERT INTO Catalog.Properties (Id, Name, Description, AddressLine1, TelephoneNumber, CloudbedsPropertyId, CloudbedsApiKey, TenantId, CreatedBy, CreatedOn, LastModifiedBy)
                VALUES (NEWID(), {0}, 'A hostel', '1 Test Street', '000', {1}, 'key', 'fuse', @empty, @now, @empty),
                       (NEWID(), 'Offline Hostel', 'A hostel', '1 Test Street', '000', 'cb-down', 'key', 'fuse', @empty, @now, @empty);
                """, new object[] { $"Hostel {run}", $"cb-{run}" }, Ct);
        }
        Cloudbeds.Reservations[$"cb-{run}"] = new()
        {
            [guestId] =
            [
                new CloudbedsReservation("R1", "checked_out", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 4), 0, "Hostelworld", [new("Dorm", null)]),
                new CloudbedsReservation("R2", "checked_in", new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 25), 12.5m, "Direct",
                    [new("6 Bed Dorm", "R307 - Bed 109"), new("6 Bed Dorm", "R307 - Bed 111"), new("Private", "101")]),
            ],
        };

        var result = await (await app.AdminApiAsync()).Send(new ListGuestHostelStays(id), Ct);

        result.Linked.ShouldBeTrue();
        result.Unreachable.ShouldContain("Offline Hostel");
        result.Stays.Select(s => s.ReservationId).ShouldBe(["R2", "R1"]); // newest first
        result.Stays[0].ShouldBe(new GuestHostelStay
        {
            Hostel = $"Hostel {run}", ReservationId = "R2", CheckIn = new DateOnly(2026, 9, 20), CheckOut = new DateOnly(2026, 9, 25),
            Nights = 5, Status = "In house", Rooms = "6 Bed Dorm ×2 · R307 - Bed 109, R307 - Bed 111; Private · 101", Balance = 12.5m, Source = "Direct",
            Url = $"https://hotels.cloudbeds.com/connect/cb-{run}#/reservations/rR2",
        });
        result.Stays[1].Status.ShouldBe("Checked out");
        Cloudbeds.Reservations.TryRemove($"cb-{run}", out _);
    }

    [Fact]
    public async Task A_guest_not_linked_to_Cloudbeds_has_no_stays_to_ask_for()
    {
        var api = await app.AdminApiAsync();
        var id = await api.Send(new CreateGuest(new UserProfile { FirstName = "No", LastName = "Stays", Email = $"nostays-{Guid.NewGuid():N}@tests.invalid" }), Ct);

        var result = await api.Send(new ListGuestHostelStays(id), Ct);

        (result.Linked, result.Stays.Count, result.Unreachable.Count).ShouldBe((false, 0, 0));
    }
}
