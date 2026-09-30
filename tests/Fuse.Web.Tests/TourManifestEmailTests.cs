using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Application.Common.Jobs;
using Fuse.Application.Features.Manifest;
using Fuse.Infrastructure.Persistence;
using Fuse.Web.Jobs;

namespace Fuse.Web.Tests;

/// <summary>
/// The tour manifest emails, as legacy's "Tours Daily Manifest": scheduled at midnight for 1 hour before each of today's
/// TourDates rows, once per location of its tour; each goes to that location's hostels.
/// </summary>
public sealed class TourManifestEmailTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A new location with a hostel that has its own email address.</summary>
    private async Task<(DefaultIdType LocationId, string HostelEmail)> LocationAsync(AppDbContext db)
    {
        var location = Guid.NewGuid();
        var email = $"hostel-{Guid.NewGuid():N}@tests.invalid";
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @now datetime2 = SYSUTCDATETIME(), @empty uniqueidentifier = '00000000-0000-0000-0000-000000000000', @hostel uniqueidentifier = NEWID();
            INSERT INTO Catalog.Destinations (Id, Name, Description, TenantId, CreatedBy, CreatedOn, LastModifiedBy)
            VALUES ({0}, 'Manifest Location', 'A place', 'fuse', @empty, @now, @empty);
            INSERT INTO Catalog.Properties (Id, Name, Description, AddressLine1, TelephoneNumber, EmailAddress, TenantId, CreatedBy, CreatedOn, LastModifiedBy)
            VALUES (@hostel, 'Manifest Hostel', 'A hostel', '1 Test Street', '000', {1}, 'fuse', @empty, @now, @empty);
            INSERT INTO Catalog.PropertyDestinationLookups (Id, PropertyId, DestinationId, CreatedBy, CreatedOn, LastModifiedBy)
            VALUES (NEWID(), @hostel, {0}, @empty, @now, @empty);
            """, new object[] { location, email }, Ct);
        return (location, email);
    }

    /// <summary>
    /// A tour departing at <paramref name="start"/> in <paramref name="locations"/> new locations (only), each with a
    /// hostel with its own email address.
    /// </summary>
    private async Task<(DefaultIdType TourId, DefaultIdType DepartureId, string TourName, (DefaultIdType Id, string Hostel)[] Locations)> TourAsync(
        DateTime start, int locations = 1)
    {
        var (tourId, departureId) = await TestBookings.TourWithDepartureAsync(app, await app.AdminApiAsync(), start, Ct);
        await using var scope = TestBookings.TenantScope(app);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Only in its own locations: the shared helper puts every test tour in the first one, which other tests' hostels
        // (with email addresses) may share, and they'd be sent the manifest too.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Catalog.TourDestinationLookups WHERE TourId = {0}", [tourId], Ct);
        var added = new List<(DefaultIdType, string)>();
        for (var i = 0; i < locations; i++)
        {
            var (location, email) = await LocationAsync(db);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO Catalog.TourDestinationLookups (Id, TourId, DestinationId, TenantId, CreatedBy, CreatedOn, LastModifiedBy)
                VALUES (NEWID(), {0}, {1}, 'fuse', '00000000-0000-0000-0000-000000000000', SYSUTCDATETIME(), '00000000-0000-0000-0000-000000000000');
                """, [tourId, location], Ct);
            added.Add((location, email));
        }
        var name = await db.Tours.Where(t => t.Id == tourId).Select(t => t.Name).SingleAsync(Ct);
        return (tourId, departureId, name, [.. added]);
    }

    private async Task<bool> SendAsync(TourManifestDeparture departure)
    {
        await using var scope = TestBookings.TenantScope(app);
        return await scope.ServiceProvider.GetRequiredService<TourManifestEmails>().SendAsync(departure, Ct);
    }

    private IReadOnlyList<Application.Common.Mail.EmailMessage> Sent(string to) => [.. app.Emails.Sent.Where(m => m.To.Contains(to))];

    private static DateTime OnTheMinute(DateTime time) => time.AddTicks(-(time.Ticks % TimeSpan.TicksPerMinute));

    [Fact]
    public async Task A_departures_manifest_goes_to_its_locations_hostels_worded_as_legacy()
    {
        var start = TestBookings.BusinessNow.Date.AddDays(1).AddHours(9);
        var (tourId, departureId, name, locations) = await TourAsync(start);
        await TestBookings.BookAsync(app, tourId, departureId, start, TestBookings.BusinessNow, Ct);
        await TestBookings.BookAsync(app, tourId, departureId, start, TestBookings.BusinessNow, Ct, paid: false);
        var hostel = locations[0].Hostel;

        (await SendAsync(new TourManifestDeparture(tourId, departureId, locations[0].Id, name, start))).ShouldBeTrue();

        var email = Sent(hostel).ShouldHaveSingleItem();
        email.Automated.ShouldBeTrue(); // so it's never sent outside production
        email.Subject.ShouldBe($"Today's Tour Manifests for {name} - 09:00");
        email.HtmlBody.ShouldContain($"Please find attached Today&#x27;s Tour Manifest for {name} - 09:00:00.");
        email.HtmlBody.ShouldContain("There are a total of 4 guests in attendance. 2 guests have paid &amp; 2 guests have yet to pay.");
        var file = email.Attachments.ShouldHaveSingleItem();
        file.FileName.ShouldBe($"{name} 09-00.xlsx");
        // As legacy: no guest-facing sign-off, and no payment method (nothing paid through a payment) reads "Stripe".
        email.HtmlBody.ShouldNotContain("THE FUSE CREW");
        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(file.Content));
        var sheet = workbook.Worksheet("table");
        sheet.RowsUsed().Count().ShouldBe(5);
        var method = sheet.Row(1).CellsUsed().Single(c => c.GetString() == "Payment Method").Address.ColumnNumber;
        sheet.Column(method).CellsUsed().Skip(1).Select(c => c.GetString()).Distinct().ShouldBe(["Stripe"]);
    }

    [Fact]
    public async Task Each_location_is_sent_on_its_own_with_only_its_TourDates_rows_guests()
    {
        var start = TestBookings.BusinessNow.Date.AddDays(1).AddHours(10);
        var (tourId, departureId, name, locations) = await TourAsync(start, locations: 2);
        await TestBookings.BookAsync(app, tourId, departureId, start, TestBookings.BusinessNow, Ct);

        (await SendAsync(new TourManifestDeparture(tourId, departureId, locations[0].Id, name, start))).ShouldBeTrue();
        Sent(locations[0].Hostel).ShouldHaveSingleItem().HtmlBody.ShouldContain("There are a total of 2 guests");
        Sent(locations[1].Hostel).ShouldBeEmpty(); // its own job sends it

        // Another TourDates row (price) of the same departure: only its own guests, so none here, and nothing is sent.
        (await SendAsync(new TourManifestDeparture(tourId, Guid.NewGuid(), locations[1].Id, name, start))).ShouldBeFalse();
        Sent(locations[1].Hostel).ShouldBeEmpty();
    }

    [Fact]
    public async Task Todays_departures_are_scheduled_for_an_hour_before_once_per_location()
    {
        var now = TestBookings.BusinessNow;
        if (now.Hour >= 22)
            Assert.Skip("Needs departures later today (Vietnam time).");
        var later = now.Date.AddHours(23).AddMinutes(30);
        var (laterTour, laterDeparture, _, laterLocations) = await TourAsync(later, locations: 2); // no bookings yet: still scheduled
        var (soonTour, _, _, _) = await TourAsync(OnTheMinute(now.AddMinutes(30))); // its hour before has passed

        await using (var scope = TestBookings.TenantScope(app))
            await scope.ServiceProvider.GetRequiredService<RecurringJobs>().ScheduleTourManifestsAsync(Ct);

        var scheduled = ((InlineJobs)app.Services.GetRequiredService<IBackgroundJobs>()).Scheduled
            .Where(s => s.Job.Method.Name == nameof(RecurringJobs.SendTourManifestAsync))
            .Select(s => (Departure: (TourManifestDeparture)s.Job.Args[0], s.Delay))
            .ToList();
        var ours = scheduled.Where(s => s.Departure.TourId == laterTour).ToList();
        ours.Select(s => s.Departure.DestinationId).Order().ShouldBe(laterLocations.Select(l => l.Id).Order());
        ours.ShouldAllBe(s => s.Departure.TourDateId == laterDeparture && s.Departure.StartDate == later);
        ours.ShouldAllBe(s => Math.Abs((s.Delay - (later.AddHours(-1) - now)).TotalMinutes) < 2);
        scheduled.ShouldNotContain(s => s.Departure.TourId == soonTour);
    }
}
