using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Admin.Pages.Tours;
using Fuse.Contracts.Bookings;
using Fuse.Contracts.Identity;
using Fuse.Contracts.Locations;
using Fuse.Contracts.Reports;
using Fuse.Contracts.Tours;
using Fuse.Domain.Identity;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Authorization;
using Fuse.Shared.Enums;

namespace Fuse.Web.Tests;

/// <summary>
/// People are shown (and copied onto bookings) by first + last name, as legacy showed them, never by the stored
/// FullName column, which is stale for many older accounts. FullName stays the search/sort column and is kept in step.
/// </summary>
public sealed class PersonNameTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void A_display_name_is_the_trimmed_first_and_last_name_falling_back_to_the_stored_one()
    {
        PersonNames.Display(" Thành ", " Phạm ", "Thanh the Pham").ShouldBe("Thành Phạm");
        PersonNames.Display("Thành", null, "Thanh the Pham").ShouldBe("Thành");
        PersonNames.Display("  ", "Phạm").ShouldBe("Phạm");
        PersonNames.Display(null, " ", " Stored Name ").ShouldBe("Stored Name");
        PersonNames.Display(null, null).ShouldBe("");
    }

    /// <summary>A person whose stored FullName is stale (as ~800 legacy accounts are); unique by <paramref name="run"/>.</summary>
    private async Task<ApplicationUser> StaleAsync(string run, string role, string first = "Thành", string last = "Phạm")
    {
        await using var scope = TestBookings.TenantScope(app);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{role.ToLowerInvariant()}-{run}@tests.invalid";
        var user = new ApplicationUser
        {
            UserName = email, Email = email, FirstName = first, LastName = $"{last}{run}", FullName = $"Thanh the Pham{run}",
            IsActive = true, EmailConfirmed = true,
        };
        (await users.CreateAsync(user, TestApp.Password)).Succeeded.ShouldBeTrue();
        (await users.AddToRoleAsync(user, role)).Succeeded.ShouldBeTrue();
        return user;
    }

    private static string Run() => Guid.NewGuid().ToString("N")[..10];

    private async Task<string?> StoredFullNameAsync(string id)
    {
        await using var scope = TestBookings.TenantScope(app);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Id == id).Select(u => u.FullName).SingleAsync(Ct);
    }

    [Fact]
    public async Task Staff_and_guest_lists_and_profiles_show_first_and_last_name_while_search_stays_on_the_stored_one()
    {
        var run = Run();
        var guest = await StaleAsync(run, FuseRoles.Guest);
        var staff = await StaleAsync(run, FuseRoles.Basic, first: "Lan", last: "Trần");
        var api = await app.AdminApiAsync();

        (await api.Send(new SearchGuests { Keyword = $"Thanh the Pham{run}" }, Ct)).Items.ShouldHaveSingleItem().FullName.ShouldBe($"Thành Phạm{run}");
        (await api.Send(new FindGuests($"Thanh the Pham{run}"), Ct)).ShouldHaveSingleItem().FullName.ShouldBe($"Thành Phạm{run}");
        (await api.Send(new GetGuest(guest.Id), Ct)).FullName.ShouldBe($"Thành Phạm{run}");

        (await api.Send(new SearchStaff { Keyword = $"Thanh the Pham{run}" }, Ct)).Items.ShouldHaveSingleItem().FullName.ShouldBe($"Lan Trần{run}");
        (await api.Send(new GetStaff(staff.Id), Ct)).FullName.ShouldBe($"Lan Trần{run}");

        // The signed-in user's name (the admin header) is first + last too, as legacy's PersonCard.
        await using var scope = TestBookings.TenantScope(app);
        var principal = await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>().CreateAsync(staff);
        principal.GetFullName().ShouldBe($"Lan Trần{run}");
    }

    [Fact]
    public async Task Saving_a_profile_brings_the_stored_full_name_into_step()
    {
        var run = Run();
        var guest = await StaleAsync(run, FuseRoles.Guest);
        var api = await app.AdminApiAsync();
        var profile = (await api.Send(new GetGuest(guest.Id), Ct)).Profile;

        await api.Send(new UpdateGuest(guest.Id, profile with { FirstName = "  Thành ", LastName = $" Phạm{run}  " }), Ct);

        (await StoredFullNameAsync(guest.Id)).ShouldBe($"Thành Phạm{run}");
    }

    [Fact]
    public async Task Bookings_show_and_copy_first_and_last_name()
    {
        var run = Run();
        var lead = await StaleAsync(run, FuseRoles.Guest);
        var extra = await StaleAsync(run + "x", FuseRoles.Guest, first: "Mai");
        var seller = await StaleAsync(run, FuseRoles.Basic, first: "Lan", last: "Trần");
        var api = await app.AdminApiAsync();

        var draft = TourDraft.New();
        draft.Details.Name = $"Name tour {run}";
        draft.Details.Description = "<p>Tour</p>";
        draft.Details.HourDuration = "2";
        draft.Details.MaxCapacity = 10;
        draft.Details.LocationIds = [(await api.Send(new ListLocations(), Ct))[0].Id];
        draft.Details.PickupPropertyIds = [Guid.Parse((await api.Send(new ListPickupProperties(), Ct)).Single(p => p.Name == TestApp.SeedPropertyName).Id)];
        draft.SavePrice(null, new TourPriceInput { Title = "Per person", Price = 20, HourDuration = 2, PublishToWebsite = true });
        draft.AddDates([TestBookings.BusinessNow.Date.AddDays(12).AddHours(9)], [draft.Prices[0].Key], null);
        var tourId = await api.Send(new CreateTour(draft.ToChanges()), Ct);
        var departure = (await api.Send(new ListTourDepartures(tourId), Ct))[0];

        var id = await api.Send(new SaveBooking(new BookingEditor
        {
            Description = "Name booking",
            GuestId = lead.Id,
            BookingSource = "Online",
            PaymentMethod = BookingPaymentMethod.Cash,
            Lines =
            [
                new BookingLine
                {
                    Key = Guid.NewGuid(), IsNew = true, TourId = tourId, TourDateId = departure.Id, StartDate = departure.StartDate,
                    PickupLocation = TestApp.SeedPropertyName, GuestIds = [extra.Id],
                },
            ],
        }), Ct);

        // The name copied onto the new booking.
        await using (var scope = TestBookings.TenantScope(app))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Bookings.Where(b => b.Id == id).Select(b => b.GuestName).SingleAsync(Ct)).ShouldBe($"Thành Phạm{run}");
            // Credited to a staff member whose stored name is stale, too.
            await db.Database.ExecuteSqlRawAsync("UPDATE Catalog.Bookings SET CreatedBy = {0} WHERE Id = {1}", new object[] { seller.Id, id }, Ct);
        }

        var editor = await api.Send(new GetBookingEditor(id), Ct);
        (editor.GuestName, editor.BookedByName, editor.GuestNames[extra.Id].Name)
            .ShouldBe(($"Thành Phạm{run}", $"Lan Trần{run}", $"Mai Phạm{run}x"));

        var overview = await api.Send(new GetBookingOverview(id), Ct);
        (overview.GuestName, overview.BookedBy).ShouldBe(($"Thành Phạm{run}", $"Lan Trần{run}"));
        overview.Items.ShouldHaveSingleItem().Guests.Select(g => g.Name).ShouldBe([$"Thành Phạm{run}", $"Mai Phạm{run}x"], ignoreOrder: true);

        // The staff bookings report (its proc searches and sorts by the stored name) shows them by first + last.
        var today = DateOnly.FromDateTime(TestBookings.BusinessNow);
        var row = (await api.Send(new SearchStaffBookings { From = today.AddDays(-1), To = today, Keyword = $"Thanh the Pham{run}" }, Ct)).Items.ShouldHaveSingleItem();
        row.FullName.ShouldBe($"Lan Trần{run}");
    }

    [Fact]
    public async Task The_public_APIs_user_full_name_is_the_stored_one_as_legacys()
    {
        var run = Run();
        var guest = await StaleAsync(run, FuseRoles.Guest);
        var api = await app.PublicApiAsync();

        var user = await (await api.GetAsync($"/api/users/{guest.Id}", Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        (user.GetProperty("fullName").GetString(), user.GetProperty("firstName").GetString()).ShouldBe(($"Thanh the Pham{run}", "Thành"));
    }
}
