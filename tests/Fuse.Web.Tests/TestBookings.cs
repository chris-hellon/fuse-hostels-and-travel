using Finbuckle.MultiTenant.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Fuse.Admin.Pages.Tours;
using Fuse.Contracts.Api;
using Fuse.Contracts.Locations;
using Fuse.Contracts.Tours;
using Fuse.Domain.Catalog.Bookings;
using Fuse.Domain.Identity;
using Fuse.Infrastructure.Multitenancy;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Multitenancy;

namespace Fuse.Web.Tests;

/// <summary>Tours with departures and bookings on them, written straight to the test database (bookings aren't rebuilt yet).</summary>
public static class TestBookings
{
    /// <summary>Now in Vietnam (the business clock the dashboard uses for "today").</summary>
    public static DateTime BusinessNow => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh")).DateTime;

    /// <summary>A service scope for the fuse tenant (the tenant context doesn't flow into test code).</summary>
    public static AsyncServiceScope TenantScope(TestApp app)
    {
        var scope = app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>().MultiTenantContext = new MultiTenantContext<FuseTenantInfo>(
            new FuseTenantInfo { Id = MultitenancyConstants.Fuse.Id, Identifier = MultitenancyConstants.Fuse.Identifier });
        return scope;
    }

    /// <summary>A tour (price $25, optionally supplied by someone) with one departure at <paramref name="start"/>.</summary>
    public static async Task<(DefaultIdType TourId, DefaultIdType TourDateId)> TourWithDepartureAsync(TestApp app, IApi api, DateTime start, CancellationToken Ct, string? supplierId = null)
    {
        var draft = TourDraft.New();
        draft.Details.Name = $"Dash tour {Guid.NewGuid():N}";
        draft.Details.Description = "<p>Tour</p>";
        draft.Details.HourDuration = "2";
        draft.Details.SupplierId = supplierId;
        draft.Details.LocationIds = [(await api.Send(new ListLocations(), Ct))[0].Id];
        draft.Details.PickupPropertyIds = [Guid.Parse((await api.Send(new ListPickupProperties(), Ct)).Single(p => p.Name == TestApp.SeedPropertyName).Id)];
        draft.SavePrice(null, new TourPriceInput { Title = "Per person", Price = 25, HourDuration = 2, PublishToWebsite = true });
        draft.AddDates([start], [draft.Prices[0].Key], null);
        var tourId = await api.Send(new CreateTour(draft.ToChanges()), Ct);

        await using var scope = TenantScope(app);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dateId = await db.TourDates.Where(d => d.TourId == tourId).Select(d => d.Id).SingleAsync(Ct);
        return (tourId, dateId);
    }

    /// <summary>A booking of the tour for the test guest (+ one extra guest), made at <paramref name="bookedAt"/>.</summary>
    public static async Task<int> BookAsync(TestApp app, DefaultIdType tourId, DefaultIdType tourDateId, DateTime start, DateTime bookedAt, CancellationToken Ct, bool paid = true,
        string? guestEmail = null, string? onTourEmail = null)
    {
        await using var scope = TenantScope(app);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var guest = await users.FindByEmailAsync(guestEmail ?? TestApp.GuestEmail);
        var admin = await users.FindByEmailAsync(TestApp.AdminEmail);
        var onTour = onTourEmail is null ? admin : await users.FindByEmailAsync(onTourEmail);

        var item = new BookingItem(start, start.AddHours(2), 50, null, null, tourId, tourDateId, null, null, "Test Hostel", true, null, Guid.Parse(admin!.Id))
        {
            Guests = [new BookingItemGuest(Guid.Parse(onTour!.Id))],
        };
        var booking = new Booking("Dash booking", 50, "USD", 1, paid, bookedAt, guest!.Id, null, true, "Test Guest", TestApp.GuestEmail, null, "Website", null, paid ? 0 : 50)
        {
            Items = [item],
            Payments = [],
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync(Ct);
        return booking.InvoiceId;
    }
}
