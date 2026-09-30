using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Contracts.Common;
using Fuse.Contracts.Reports;
using Fuse.Contracts.Site;
using Fuse.Domain.Identity;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Authorization;

namespace Fuse.Web.Tests;

/// <summary>Reports › Staff Bookings: paid sales credited to staff members, with commission (GetStaffBookingsReport).</summary>
public sealed class StaffBookingsReportTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Today => DateOnly.FromDateTime(TestBookings.BusinessNow);

    /// <summary>A new staff member (Basic role) with a unique name to search for.</summary>
    private async Task<(DefaultIdType Id, string Name)> StaffMemberAsync()
    {
        await using var scope = TestBookings.TenantScope(app);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var last = $"Seller{Guid.NewGuid():N}"[..20];
        var user = new ApplicationUser
        {
            UserName = $"{last}@tests.invalid", Email = $"{last}@tests.invalid", FirstName = "Kim", LastName = last, FullName = $"Kim {last}",
            IsActive = true, EmailConfirmed = true,
        };
        (await users.CreateAsync(user, TestApp.Password)).Succeeded.ShouldBeTrue();
        await users.AddToRoleAsync(user, FuseRoles.Basic);
        return (DefaultIdType.Parse(user.Id), user.FullName!);
    }

    /// <summary>A departure whose price pays $3 commission a guest.</summary>
    private async Task<(DefaultIdType TourId, DefaultIdType DepartureId, DateTime Start, string TourName)> CommissionTourAsync(bool published = false)
    {
        var start = TestBookings.BusinessNow.Date.AddDays(3).AddHours(9);
        var (tourId, departureId) = await TestBookings.TourWithDepartureAsync(app, await app.AdminApiAsync(), start, Ct);
        await using var scope = TestBookings.TenantScope(app);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("UPDATE Catalog.TourPrices SET ComissionAmount = 3 WHERE TourId = {0}", new object[] { tourId }, Ct);
        if (published)
            await db.Database.ExecuteSqlRawAsync("UPDATE Catalog.Tours SET PublishToSite = 1 WHERE Id = {0}", new object[] { tourId }, Ct);
        return (tourId, departureId, start, await db.Tours.Where(t => t.Id == tourId).Select(t => t.Name).SingleAsync(Ct));
    }

    private async Task CreditAsync(int invoiceId, DefaultIdType staffId)
    {
        await using var scope = TestBookings.TenantScope(app);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .ExecuteSqlRawAsync("UPDATE Catalog.Bookings SET CreatedBy = {0} WHERE InvoiceId = {1}", new object[] { staffId, invoiceId }, Ct);
    }

    [Fact]
    public async Task A_staff_members_paid_sales_show_with_their_commission_and_tour_lines()
    {
        var (staffId, name) = await StaffMemberAsync();
        var (tourId, departureId, start, tourName) = await CommissionTourAsync();
        var bookedAt = TestBookings.BusinessNow.AddHours(-1);
        await CreditAsync(await TestBookings.BookAsync(app, tourId, departureId, start, bookedAt, Ct), staffId);
        await CreditAsync(await TestBookings.BookAsync(app, tourId, departureId, start, bookedAt, Ct, paid: false), staffId); // unpaid: not a sale
        var api = await app.AdminApiAsync();

        var result = await api.Send(new SearchStaffBookings { From = Today.AddDays(-1), To = Today, Keyword = name }, Ct);

        var row = result.Items.ShouldHaveSingleItem();
        (row.UserId, row.FullName, row.BookingsMade, row.ItemsCount).ShouldBe((staffId, name, 1, 2)); // guests
        row.TotalBookingsAmount.ShouldBe(100m); // $50 a head, the booker + one guest
        row.TotalCommission.ShouldBe(6m);
        var line = row.Lines.ShouldHaveSingleItem();
        (line.TourName, line.QuantityBooked, line.EligiblePax, line.TotalAmount, line.TotalCommissionAmount).ShouldBe((tourName, 2, 2, 100m, 6m));

        // Outside the dates: nothing.
        (await api.Send(new SearchStaffBookings { From = Today.AddDays(-10), To = Today.AddDays(-5), Keyword = name }, Ct)).Items.ShouldBeEmpty();

        // Legacy's export: a row per staff member, their totals as plain numbers.
        var file = await api.Send(new ExportStaffBookings(Today.AddDays(-1), Today, null, name), Ct);
        file.FileName.ShouldBe("Staff Bookings.xlsx");
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var sheet = workbook.Worksheet("table");
        sheet.Tables.ShouldHaveSingleItem().Name.ShouldBe("table");
        Enumerable.Range(1, 6).Select(c => sheet.Cell(1, c).GetString())
            .ShouldBe(["Staff Name", "Total Bookings Made", "Total Items Booked", "Total Bookings Revenue", "Total Commission Amount", ""]);
        (sheet.Cell(2, 1).GetString(), sheet.Cell(2, 2).GetDouble(), sheet.Cell(2, 3).GetDouble(), sheet.Cell(2, 4).GetDouble(), sheet.Cell(2, 5).GetDouble())
            .ShouldBe((name, 1d, 2d, 100d, 6d));
        sheet.Cell(3, 1).IsEmpty().ShouldBeTrue();
    }

    [Fact]
    public async Task A_paid_website_booking_from_a_staff_qr_code_is_credited_to_them()
    {
        var (staffId, name) = await StaffMemberAsync();
        var (_, departureId, _, tourName) = await CommissionTourAsync(published: true);
        var anonymous = new Fuse.Web.Client.Api.HttpApi(app.Anonymous());
        var placed = await anonymous.Send(new PlaceBooking(new CheckoutContact
        {
            FirstName = "Sam", LastName = "Walker", Email = $"qr-{Guid.NewGuid():N}@tests.invalid", PhoneNumber = "+84 1",
            DateOfBirth = new DateTime(1995, 5, 5), Gender = "Female", Nationality = "Australia",
        }, [], [new CheckoutTour { DepartureId = departureId }], null, null, null, null, staffId), Ct);
        await using (var scope = TestBookings.TenantScope(app))
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
                .ExecuteSqlRawAsync("UPDATE Catalog.Bookings SET IsPaid = 1 WHERE Id = {0}", new object[] { placed.BookingId }, Ct);

        var row = (await (await app.AdminApiAsync()).Send(new SearchStaffBookings { From = Today, To = Today, Keyword = name }, Ct)).Items.ShouldHaveSingleItem();

        row.BookingsMade.ShouldBe(1);
        row.Lines.ShouldHaveSingleItem().TourName.ShouldBe(tourName);
        row.TotalCommission.ShouldBe(3m); // one guest
    }

    [Fact]
    public async Task The_dates_must_be_in_order()
    {
        var api = await app.AdminApiAsync();
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SearchStaffBookings { From = Today, To = Today.AddDays(-1) }, Ct).AsTask());
    }
}
