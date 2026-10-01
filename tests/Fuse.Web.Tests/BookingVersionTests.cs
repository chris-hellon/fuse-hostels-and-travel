using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Admin.Pages.Bookings;
using Fuse.Contracts.Api;
using Fuse.Contracts.Bookings;
using Fuse.Contracts.Common;
using Fuse.Contracts.Dashboard;
using Fuse.Contracts.Identity;
using Fuse.Contracts.Manifest;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Enums;

namespace Fuse.Web.Tests;

/// <summary>
/// The booking's concurrency version: every change made outside the booking wizard moves it on, so a wizard opened before
/// can't save over it (and notices, by polling <see cref="GetBookingVersion"/>); the wizard's own refunds and cancellations don't.
/// </summary>
public sealed class BookingVersionTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A departure in <paramref name="days"/> days with a booking on it for two guests (lead + one), at $50 each.</summary>
    private async Task<(IApi Api, BookingRow Booking, DefaultIdType TourId, DefaultIdType TourDateId, DateTime Start)> BookedAsync(bool paid, int days = 12)
    {
        var api = await app.AdminApiAsync();
        var start = TestBookings.BusinessNow.Date.AddDays(days).AddHours(9);
        var (tourId, dateId) = await TestBookings.TourWithDepartureAsync(app, api, start, Ct);
        var reference = await TestBookings.BookAsync(app, tourId, dateId, start, TestBookings.BusinessNow, Ct, paid);
        var row = (await api.Send(new SearchBookings { Keyword = reference.ToString(), PageSize = 100 }, Ct)).Items.Single(r => r.Reference == reference);
        return (api, row, tourId, dateId, start);
    }

    /// <summary>A cash (so paid) booking made by the wizard: one guest on two tours' $25 departures in <paramref name="days"/> days.</summary>
    private async Task<(IApi Api, DefaultIdType Id)> WizardBookingAsync(int days)
    {
        var api = await app.AdminApiAsync();
        var lines = new List<BookingLine>();
        for (var i = 0; i < 2; i++)
        {
            var (tourId, _) = await TestBookings.TourWithDepartureAsync(app, api, TestBookings.BusinessNow.Date.AddDays(days).AddHours(9), Ct);
            var departure = (await api.Send(new ListTourDepartures(tourId), Ct)).Single();
            lines.Add(new BookingLine { Key = Guid.NewGuid(), IsNew = true, TourId = tourId, TourDateId = departure.Id, StartDate = departure.StartDate, PickupLocation = TestApp.SeedPropertyName });
        }
        var guest = await api.Send(new CreateGuest(new UserProfile { FirstName = "Version", LastName = "Guest", Email = $"guest-{Guid.NewGuid():N}@tests.invalid" }), Ct);
        var id = await api.Send(new SaveBooking(new BookingEditor
        {
            Description = "Wizard booking",
            GuestId = guest,
            BookingSource = "Online",
            PaymentMethod = BookingPaymentMethod.Cash,
            Lines = lines,
        }), Ct);
        return (api, id);
    }

    private async Task<BookingVersion> VersionAsync(IApi api, DefaultIdType id) => await api.Send(new GetBookingVersion(id), Ct);

    /// <summary>Runs <paramref name="change"/> and checks it moved the version on by exactly one.</summary>
    private async Task<BookingVersion> BumpsAsync(IApi api, DefaultIdType id, Func<Task> change)
    {
        var before = await VersionAsync(api, id);
        await change();
        var after = await VersionAsync(api, id);
        after.Version.ShouldBe(before.Version + 1);
        return after;
    }

    [Fact]
    public async Task Payments_refunds_cancellations_and_attendance_each_move_the_version_on()
    {
        var (api, booking, tourId, dateId, start) = await BookedAsync(paid: false);
        var loaded = await VersionAsync(api, booking.Id);
        (loaded.Version, (await api.Send(new GetBookingEditor(booking.Id), Ct)).ConcurrencyVersion).ShouldBe((0, 0));
        (loaded.State, loaded.Outstanding, loaded.Payments, loaded.Tours).ShouldBe((BookingState.PartiallyPaid, 50m, 0, 1));

        var paid = await BumpsAsync(api, booking.Id, () => api.Send(new AddBookingPayment(booking.Id, 20, BookingPaymentMethod.Cash, null), Ct).AsTask());
        (paid.Outstanding, paid.Payments).ShouldBe((30m, 1));
        BookingVersion.Changes(loaded, paid).ShouldContain("a payment was added");

        // A card payment through a payment link: the link itself (the checkout it records) changes nothing the wizard shows.
        var session = (await api.Send(new CreatePaymentLink(booking.Id), Ct)).Url.Split('/')[^1];
        (await VersionAsync(api, booking.Id)).Version.ShouldBe(paid.Version);
        app.Payments.MarkPaid(session);
        var anonymous = new Fuse.Web.Client.Api.HttpApi(app.Anonymous());
        var settled = await BumpsAsync(api, booking.Id, async () => (await anonymous.Send(new ConfirmCheckout(session), Ct)).Confirmed.ShouldBeTrue());
        (settled.State, settled.Payments).ShouldBe((BookingState.Paid, 2));
        await anonymous.Send(new ConfirmCheckout(session), Ct); // already recorded: nothing changes
        (await VersionAsync(api, booking.Id)).Version.ShouldBe(settled.Version);

        DefaultIdType cardPayment;
        await using (var scope = TestBookings.TenantScope(app))
            cardPayment = await scope.ServiceProvider.GetRequiredService<AppDbContext>().BookingPayments
                .Where(p => p.BookingId == booking.Id && p.StripeSessionId == session).Select(p => p.Id).SingleAsync(Ct);
        var paymentRefunded = await BumpsAsync(api, booking.Id, () => api.Send(new RefundBookingPayment(cardPayment, 10), Ct).AsTask());
        paymentRefunded.RefundedPayments.ShouldBe(1);
        BookingVersion.Changes(settled, paymentRefunded).ShouldContain("something was refunded");

        await BumpsAsync(api, booking.Id, () => api.Send(new RefundBooking(booking.Id, 5), Ct).AsTask());

        var overview = await api.Send(new GetBookingOverview(booking.Id), Ct);
        var extra = overview.Items.Single().Guests.First(g => g.GuestId != overview.GuestId).GuestId;
        var attendance = await BumpsAsync(api, booking.Id,
            () => api.Send(new SetGuestAttendance(booking.Reference, tourId, start, dateId, extra, GuestAttendance.CheckedIn), Ct).AsTask());
        attendance.CheckedIn.ShouldBe(1);
        BookingVersion.Changes(paymentRefunded, attendance).ShouldContain("guests were checked in, marked no show or cancelled");

        var item = (await api.Send(new GetBookingOverview(booking.Id), Ct)).Items.Single();
        var tourRefunded = await BumpsAsync(api, booking.Id, () => api.Send(new RefundBookingItem(item.Id, 10), Ct).AsTask());
        tourRefunded.RefundedTours.ShouldBe(1);
        var tourCancelled = await BumpsAsync(api, booking.Id, () => api.Send(new CancelBookingItem(item.Id, 0), Ct).AsTask());
        (tourCancelled.CancelledTours, tourCancelled.State).ShouldBe((1, BookingState.Cancelled));
        BookingVersion.Changes(tourRefunded, tourCancelled).ShouldBe(["it's now Cancelled", "a tour was cancelled"]);

        var (_, other, _, _, _) = await BookedAsync(paid: true, days: 13);
        await BumpsAsync(api, other.Id, () => api.Send(new CancelBooking(other.Id, 0), Ct).AsTask());
    }

    [Fact]
    public async Task The_public_apis_flag_as_paid_moves_the_version_on()
    {
        var (api, booking, _, _, _) = await BookedAsync(paid: false, days: 14);
        var client = await app.PublicApiAsync();

        var paid = await BumpsAsync(api, booking.Id, async () =>
            (await client.PostAsJsonAsync($"/api/bookings/{booking.Id}/flag-as-paid", new { stripeSessionId = "cs_test_flagged" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK));
        paid.State.ShouldBe(BookingState.Paid);
    }

    [Fact]
    public async Task A_wizard_opened_before_a_payment_elsewhere_can_not_save()
    {
        var (api, booking, _, _, _) = await BookedAsync(paid: false, days: 15);
        var editor = await api.Send(new GetBookingEditor(booking.Id), Ct);

        await api.Send(new AddBookingPayment(booking.Id, 20, BookingPaymentMethod.Cash, null), Ct);

        editor.AdditionalNotes = "Changed in the wizard";
        var refused = await Should.ThrowAsync<ConflictException>(() => api.Send(new SaveBooking(editor), Ct).AsTask());
        refused.Message.ShouldBe("Someone else changed this booking while you were editing it. Close it and open it again.");
        (await api.Send(new GetBookingEditor(booking.Id), Ct)).AdditionalNotes.ShouldBeNull();
    }

    [Fact]
    public async Task The_wizards_own_refund_or_cancel_does_not_stop_its_save_but_someone_elses_change_does()
    {
        var (api, bookingId) = await WizardBookingAsync(16);
        var catalog = await api.Send(new GetBookingCatalog(), Ct);
        var abilities = await api.Send(new GetBookingAbilities(), Ct);

        // The wizard refunds a tour straight away: it takes the new version (its own change) and saves.
        var draft = new BookingDraft(await api.Send(new GetBookingEditor(bookingId), Ct), catalog, abilities);
        draft.Reprice();
        draft.MarkUnchanged();
        var opened = draft.Editor.ConcurrencyVersion;
        draft.Editor.AdditionalNotes = "Unsaved note";
        await api.Send(new RefundBookingItem(draft.Editor.Lines[0].Key, 10), Ct);
        draft.SavedOwnChange();
        draft.ExpectedVersion.ShouldBe((await VersionAsync(api, bookingId)).Version); // polling sees nothing to warn about
        draft.ApplySavedState(await api.Send(new GetBookingEditor(bookingId), Ct));
        (draft.Editor.ConcurrencyVersion, draft.ExpectedVersion, draft.HasChanges).ShouldBe((opened + 1, opened + 1, true));
        await api.Send(new SaveBooking(draft.Editor), Ct);
        (await api.Send(new GetBookingEditor(bookingId), Ct)).AdditionalNotes.ShouldBe("Unsaved note");

        // Someone else changes it too before the wizard takes its own change: the wizard keeps its version and can't save.
        var stale = new BookingDraft(await api.Send(new GetBookingEditor(bookingId), Ct), catalog, abilities);
        stale.Reprice();
        var version = stale.Editor.ConcurrencyVersion;
        await api.Send(new RefundBookingItem(stale.Editor.Lines[1].Key, 5), Ct);
        stale.SavedOwnChange();
        await api.Send(new RefundBooking(bookingId, 1), Ct); // elsewhere
        (await VersionAsync(api, bookingId)).Version.ShouldBeGreaterThan(stale.ExpectedVersion); // polling warns
        stale.ApplySavedState(await api.Send(new GetBookingEditor(bookingId), Ct));
        stale.Editor.ConcurrencyVersion.ShouldBe(version);
        stale.Editor.Lines[1].SavedStatus.ShouldBe(BookingState.PartialRefund); // its own refund is still shown
        stale.Editor.AdditionalNotes = "Lost";
        await Should.ThrowAsync<ConflictException>(() => api.Send(new SaveBooking(stale.Editor), Ct).AsTask());
    }

    [Fact]
    public async Task The_wizards_own_change_is_not_taken_when_someone_else_changed_the_booking_first()
    {
        var (api, bookingId) = await WizardBookingAsync(17);
        var draft = new BookingDraft(await api.Send(new GetBookingEditor(bookingId), Ct), await api.Send(new GetBookingCatalog(), Ct),
            await api.Send(new GetBookingAbilities(), Ct));
        draft.Reprice();
        var opened = draft.Editor.ConcurrencyVersion;

        await api.Send(new RefundBooking(bookingId, 1), Ct); // elsewhere, first
        await api.Send(new RefundBookingItem(draft.Editor.Lines[0].Key, 10), Ct);
        draft.SavedOwnChange();
        draft.ApplySavedState(await api.Send(new GetBookingEditor(bookingId), Ct));

        draft.Editor.ConcurrencyVersion.ShouldBe(opened);
        draft.Editor.AdditionalNotes = "Too late";
        await Should.ThrowAsync<ConflictException>(() => api.Send(new SaveBooking(draft.Editor), Ct).AsTask());
    }

    [Fact]
    public async Task The_version_is_for_staff_who_see_tour_bookings()
    {
        var (api, booking, _, _, _) = await BookedAsync(paid: true, days: 18);
        (await VersionAsync(api, booking.Id)).Version.ShouldBe(0);
        await Should.ThrowAsync<NotFoundException>(() => api.Send(new GetBookingVersion(Guid.NewGuid()), Ct).AsTask());

        var guest = new Fuse.Web.Client.Api.HttpApi(await app.SignedInAsync(TestApp.GuestEmail));
        await Should.ThrowAsync<ForbiddenException>(() => guest.Send(new GetBookingVersion(booking.Id), Ct).AsTask());
        var anonymous = new Fuse.Web.Client.Api.HttpApi(app.Anonymous());
        await Should.ThrowAsync<UnauthorizedException>(() => anonymous.Send(new GetBookingVersion(booking.Id), Ct).AsTask());
    }
}
