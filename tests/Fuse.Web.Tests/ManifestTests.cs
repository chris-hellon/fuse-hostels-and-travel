using Shouldly;
using Fuse.Contracts.Dashboard;
using Fuse.Contracts.Manifest;

namespace Fuse.Web.Tests;

public sealed class ManifestTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Todays_guests_can_be_checked_in_marked_no_show_and_cancelled()
    {
        var api = await app.AdminApiAsync();
        var start = TestBookings.BusinessNow.Date.AddHours(12).AddMinutes(Random.Shared.Next(1, 59));
        var (tourId, dateId) = await TestBookings.TourWithDepartureAsync(app, api, start, Ct);
        var reference = await TestBookings.BookAsync(app, tourId, dateId, start, TestBookings.BusinessNow, Ct);

        (await api.Send(new ListManifestTours(), Ct)).ShouldContain(t => t.Id == tourId.ToString());
        (await api.Send(new ListManifestTimes(tourId), Ct)).ShouldBe([TimeOnly.FromDateTime(start)]);

        var rows = (await api.Send(new SearchManifest { TourId = tourId, StartTime = TimeOnly.FromDateTime(start) }, Ct)).Items;
        rows.Count.ShouldBe(2); // lead guest + additional guest
        rows.ShouldAllBe(r => r.Reference == reference && r.Status == BookingState.Paid);

        // One guest checks in; the other is a no show, then cancelled.
        var (lead, extra) = (rows[0], rows[1]);
        await api.Send(new SetGuestAttendance(reference, tourId, start, dateId, extra.GuestId, GuestAttendance.CheckedIn), Ct);
        await api.Send(new SetGuestAttendance(reference, tourId, start, dateId, lead.GuestId, GuestAttendance.NoShow), Ct);

        var after = (await api.Send(new SearchManifest { TourId = tourId }, Ct)).Items;
        after.Single(r => r.GuestId == extra.GuestId).Status.ShouldBe(BookingState.CheckedIn);
        after.Single(r => r.GuestId == lead.GuestId).Status.ShouldBe(BookingState.NoShow);

        await api.Send(new SetGuestAttendance(reference, tourId, start, dateId, lead.GuestId, GuestAttendance.Cancelled), Ct);
        // As legacy: a cancelled additional guest stays listed as Cancelled; cancelling the lead guest
        // cancels the booked tour, which takes it off the manifest.
        var cancelled = (await api.Send(new SearchManifest { TourId = tourId }, Ct)).Items.SingleOrDefault(r => r.GuestId == lead.GuestId);
        (cancelled is null || cancelled.Status == BookingState.Cancelled).ShouldBeTrue();

        (await api.Send(new ExportManifest(tourId, null, null), Ct)).Content.Take(2).ShouldBe("PK"u8.ToArray());
    }
}
