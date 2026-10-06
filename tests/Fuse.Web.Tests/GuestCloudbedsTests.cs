using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Application.Common.Cloudbeds;
using Fuse.Contracts.Identity;
using Fuse.Domain.Identity;

namespace Fuse.Web.Tests;

/// <summary>Stands in for Cloudbeds: only the "cb-property" account knows guest "cb-guest-1"; guest lists are
/// whatever a test puts in <see cref="Lists"/> (property id → status → guests), and "cb-down" is unreachable.</summary>
public sealed class FakeCloudbeds : ICloudbedsClient
{
    public System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<string, IReadOnlyList<CloudbedsGuest>>> Lists { get; } = new();

    /// <summary>property id → status → its reservations (filtered by check-in dates as Cloudbeds does).</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<string, IReadOnlyList<CloudbedsReservation>>> ByStatus { get; } = new();

    /// <summary>
    /// property id → how many times its reservations were listed without rooms (the Tour Attach Rate's fetch; the other
    /// panels' lists, from tests running alongside, ask with rooms).
    /// </summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, int> ReservationListCalls { get; } = new();

    /// <summary>property id → a task its lists without rooms wait for (to hold a fetch open).</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, Task> HoldReservations { get; } = new();

    public async Task<IReadOnlyList<CloudbedsReservation>> ListReservationsAsync(string propertyId, string apiKey, string? status, DateOnly? checkInFrom,
        DateOnly? checkInTo, CancellationToken cancellationToken, bool includeRooms = true)
    {
        if (!includeRooms)
        {
            ReservationListCalls.AddOrUpdate(propertyId, 1, (_, n) => n + 1);
            if (HoldReservations.TryGetValue(propertyId, out var hold))
                await hold;
        }
        return propertyId == "cb-down" ? throw new HttpRequestException("Cloudbeds is down")
            : ByStatus.TryGetValue(propertyId, out var byStatus)
                ? [.. byStatus.Where(s => status is null || s.Key == status).SelectMany(s => s.Value)
                    .Where(r => (checkInFrom is null || r.StartDate >= checkInFrom) && (checkInTo is null || r.StartDate <= checkInTo))]
                : [];
    }

    /// <summary>property id → guest id → their reservations there.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<string, IReadOnlyList<CloudbedsReservation>>> Reservations { get; } = new();

    public Task<IReadOnlyList<CloudbedsReservation>> ListGuestReservationsAsync(string propertyId, string apiKey, string guestId, CancellationToken cancellationToken) =>
        propertyId == "cb-down" ? throw new HttpRequestException("Cloudbeds is down")
        : Reservations.TryGetValue(propertyId, out var byGuest) && byGuest.TryGetValue(guestId, out var reservations) ? Task.FromResult(reservations)
        : Task.FromResult<IReadOnlyList<CloudbedsReservation>>([]);

    public Task<IReadOnlyList<CloudbedsGuest>> ListGuestsByStatusAsync(string propertyId, string apiKey, string status, CancellationToken cancellationToken) =>
        propertyId == "cb-down" ? throw new HttpRequestException("Cloudbeds is down")
        : Lists.TryGetValue(propertyId, out var byStatus) && byStatus.TryGetValue(status, out var guests) ? Task.FromResult(guests)
        : Task.FromResult<IReadOnlyList<CloudbedsGuest>>([]);

    public Task<CloudbedsGuest?> GetGuestAsync(string propertyId, string apiKey, string guestId, CancellationToken cancellationToken) =>
        Task.FromResult(propertyId == "cb-property" && guestId == "cb-guest-1"
            ? new CloudbedsGuest
            {
                FirstName = " Jamie [2 beds]",
                LastName = "Rowe",
                Gender = "F",
                Phone = "+44 7700900123",
                Country = "GB",
                BirthDate = "1999-04-12",
                DocumentType = "Passport",
                DocumentNumber = "X12345678",
                DocumentIssuingCountry = "GB",
                DocumentExpirationDate = "2031-06-30",
                DocumentIssueDate = "",
            }
            : null);
}

public sealed class GuestCloudbedsTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_guest_without_a_Cloudbeds_link_has_nothing_to_compare()
    {
        var api = await app.AdminApiAsync();
        var id = await api.Send(new CreateGuest(new UserProfile { FirstName = "No", LastName = "Link", Email = Email() }), Ct);

        (await api.Send(new GetGuestCloudbedsProfile(id), Ct)).ShouldBe(new CloudbedsGuestLookup(null, null));
    }

    [Fact]
    public async Task A_linked_guest_gets_the_Cloudbeds_values_mapped_like_the_import()
    {
        var api = await app.AdminApiAsync();
        var id = await api.Send(new CreateGuest(new UserProfile { FirstName = "Jamie", LastName = "Rowe", Email = Email() }), Ct);
        await LinkAsync(id, "cb-guest-1");
        await using (var connection = new SqlConnection(app.ConnectionString))
        {
            // One property the fake doesn't know the guest in (skipped), one it does.
            await connection.OpenAsync(Ct);
            await using var cmd = new SqlCommand("""
                DECLARE @empty uniqueidentifier = '00000000-0000-0000-0000-000000000000';
                INSERT INTO Catalog.Properties (Id, Name, Description, AddressLine1, TelephoneNumber, TenantId, CreatedBy, CreatedOn, LastModifiedBy, CloudbedsPropertyId, CloudbedsApiKey)
                VALUES (NEWID(), 'CB Other', 'x', 'x', '0', 'fuse', @empty, GETUTCDATE(), @empty, 'cb-other', 'key'),
                       (NEWID(), 'CB Hostel', 'x', 'x', '0', 'fuse', @empty, GETUTCDATE(), @empty, 'cb-property', 'key');
                """, connection);
            await cmd.ExecuteNonQueryAsync(Ct);
        }

        var lookup = await api.Send(new GetGuestCloudbedsProfile(id), Ct);

        lookup.CloudbedsGuestId.ShouldBe("cb-guest-1");
        var p = lookup.Profile.ShouldNotBeNull();
        (p.FirstName, p.LastName, p.Gender, p.PhoneNumber, p.Nationality).ShouldBe(("Jamie", "Rowe", "Female", "+44 7700900123", "United Kingdom"));
        (p.DateOfBirth, p.PassportNumber, p.PassportIssuingCountry, p.PassportExpiryDate, p.PassportIssueDate)
            .ShouldBe((new DateTime(1999, 4, 12), "X12345678", "United Kingdom", new DateTime(2031, 6, 30), null));
    }

    [Fact]
    public async Task A_linked_guest_Cloudbeds_doesnt_know_comes_back_without_a_profile()
    {
        var api = await app.AdminApiAsync();
        var id = await api.Send(new CreateGuest(new UserProfile { FirstName = "Gone", LastName = "Guest", Email = Email() }), Ct);
        await LinkAsync(id, "cb-unknown");

        (await api.Send(new GetGuestCloudbedsProfile(id), Ct)).ShouldBe(new CloudbedsGuestLookup("cb-unknown", null));
    }

    private static string Email() => $"guest-{Guid.NewGuid():N}@tests.invalid";

    private async Task LinkAsync(string userId, string cloudbedsId)
    {
        await using var scope = TestBookings.TenantScope(app);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(userId);
        user!.CloudbedsGuestId = cloudbedsId;
        (await users.UpdateAsync(user)).Succeeded.ShouldBeTrue();
    }
}
