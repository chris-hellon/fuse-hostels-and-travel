using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Contracts.Auth;
using Fuse.Contracts.Common;
using Fuse.Contracts.Identity;
using Fuse.Contracts.Site;
using Fuse.Domain.Identity;
using Fuse.Infrastructure.Persistence;

namespace Fuse.Web.Tests;

/// <summary>The website's My Account: a guest's own bookings, paying them, their email, password and data.</summary>
public sealed partial class MyAccountTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A new guest (registered on the site), signed in.</summary>
    private async Task<(Fuse.Web.Client.Api.HttpApi Api, string Email)> GuestAsync()
    {
        var email = $"guest-{Guid.NewGuid():N}@tests.invalid";
        (await app.Anonymous().PostAsJsonAsync(AuthRoutes.Register, new RegisterGuestRequest
        {
            FirstName = "Sam", Surname = "Walker", Email = email, DateOfBirth = new DateTime(1995, 5, 5), PhoneNumber = "+84 1",
            Nationality = "Australia", Gender = "Female", Password = TestApp.Password, ConfirmPassword = TestApp.Password,
        }, Ct)).EnsureSuccessStatusCode();
        return (new Fuse.Web.Client.Api.HttpApi(await app.SignedInAsync(email)), email);
    }

    /// <summary>A booking of one departure made by the signed-in guest on the website (unpaid, awaiting Stripe).</summary>
    private async Task<DefaultIdType> BookAsync(Fuse.Web.Client.Api.HttpApi guest, string email)
    {
        var api = await app.AdminApiAsync();
        var (tourId, departureId) = await TestBookings.TourWithDepartureAsync(app, api, TestBookings.BusinessNow.Date.AddDays(5).AddHours(9), Ct);
        await DbAsync(db => db.Database.ExecuteSqlRawAsync("UPDATE Catalog.Tours SET PublishToSite = 1 WHERE Id = {0}", [tourId], Ct));
        var contact = new CheckoutContact
        {
            FirstName = "Sam", LastName = "Walker", Email = email, PhoneNumber = "+84 1", DateOfBirth = new DateTime(1995, 5, 5), Gender = "Female", Nationality = "Australia",
        };
        var friend = new CheckoutGuest { FirstName = "Alex", LastName = "Friend" };
        return (await guest.Send(new PlaceBooking(contact, [friend], [new CheckoutTour { DepartureId = departureId, GuestKeys = [friend.Key] }], null, null, "Veggie"), Ct)).BookingId;
    }

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> work)
    {
        await using var scope = TestBookings.TenantScope(app);
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task A_guest_sees_their_own_bookings_with_tours_guests_and_what_is_owed()
    {
        var (guest, email) = await GuestAsync();
        var id = await BookAsync(guest, email);

        var list = await guest.Send(new ListMyBookings(), Ct);
        var booking = await guest.Send(new GetMyBooking(id), Ct);

        list.ShouldHaveSingleItem().Id.ShouldBe(id);
        list[0].Upcoming.ShouldBeTrue();
        list[0].Status.ShouldBe(MyBookingStatus.AwaitingPayment);
        booking.Tours.ShouldHaveSingleItem().Guests.ShouldBe(2);
        booking.Tours[0].GuestNames.ShouldBe(["Sam Walker", "Alex Friend"]);
        booking.Tours[0].Slug.ShouldNotBeNull();
        booking.Outstanding.ShouldBe(booking.Total);
        booking.Notes.ShouldBe("Veggie");
        // A website checkout is paid on its own Stripe page, never a second one.
        await Should.ThrowAsync<BusinessRuleException>(() => guest.Send(new PayMyBooking(id), Ct).AsTask());
    }

    [Fact]
    public async Task Another_guests_booking_cannot_be_seen_or_paid()
    {
        var (owner, email) = await GuestAsync();
        var id = await BookAsync(owner, email);
        var (other, _) = await GuestAsync();

        (await other.Send(new ListMyBookings(), Ct)).ShouldBeEmpty();
        await Should.ThrowAsync<NotFoundException>(() => other.Send(new GetMyBooking(id), Ct).AsTask());
        await Should.ThrowAsync<NotFoundException>(() => other.Send(new PayMyBooking(id), Ct).AsTask());
        await Should.ThrowAsync<UnauthorizedException>(() => new Fuse.Web.Client.Api.HttpApi(app.Anonymous()).Send(new ListMyBookings(), Ct).AsTask());
    }

    [Fact]
    public async Task What_a_booking_still_owes_is_paid_on_stripe()
    {
        var (guest, email) = await GuestAsync();
        var id = await BookAsync(guest, email);
        await DbAsync(db => db.Database.ExecuteSqlRawAsync("UPDATE Catalog.Bookings SET BookingSource = 'Staff' WHERE Id = {0}", [id], Ct));

        (await guest.Send(new GetMyBooking(id), Ct)).Status.ShouldBe(MyBookingStatus.Due);
        (await guest.Send(new PayMyBooking(id), Ct)).ShouldStartWith("https://checkout.stripe.test/");
    }

    [Fact]
    public async Task A_payment_with_no_method_recorded_shows_as_stripe()
    {
        var (guest, email) = await GuestAsync();
        var id = await BookAsync(guest, email);
        await DbAsync(async db =>
        {
            db.BookingPayments.Add(new Fuse.Domain.Catalog.Bookings.BookingPayment(id, 10, "cs_test_old", false, "Sam Walker", email, "USD", null)
            {
                PaymentDate = TestBookings.BusinessNow,
            });
            return await db.SaveChangesAsync(Ct);
        });

        (await guest.Send(new GetMyBooking(id), Ct)).Payments.ShouldHaveSingleItem().Method.ShouldBe("Stripe");
    }

    [Fact]
    public async Task A_guest_changes_their_email_only_by_following_the_link_sent_to_it()
    {
        var (guest, email) = await GuestAsync();
        var newEmail = $"new-{Guid.NewGuid():N}@tests.invalid";
        var me = await guest.Send(new GetMyProfile(), Ct);

        // Not by editing the profile.
        await guest.Send(new UpdateMyProfile(me.Profile with { Email = newEmail, FirstName = "Samantha" }), Ct);
        (await guest.Send(new GetMyProfile(), Ct)).Profile.ShouldSatisfyAllConditions(p => p.Email.ShouldBe(email), p => p.FirstName.ShouldBe("Samantha"));

        await guest.Send(new RequestMyEmailChange(newEmail), Ct);
        var sent = app.Emails.Sent.Single(m => m.To.Contains(newEmail));
        var link = System.Net.WebUtility.HtmlDecode(Link().Match(sent.HtmlBody).Groups[1].Value);
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query);
        (await guest.Send(new GetMyProfile(), Ct)).Profile.Email.ShouldBe(email); // not until confirmed

        await Should.ThrowAsync<BusinessRuleException>(() =>
            new Fuse.Web.Client.Api.HttpApi(app.Anonymous()).Send(new ConfirmMyEmailChange(query["user"]!, newEmail, "forged"), Ct).AsTask());
        await new Fuse.Web.Client.Api.HttpApi(app.Anonymous()).Send(new ConfirmMyEmailChange(query["user"]!, query["email"]!, query["token"]!), Ct);

        (await guest.Send(new GetMyProfile(), Ct)).Profile.Email.ShouldBe(newEmail);
        await app.SignedInAsync(newEmail); // signs in with it
        (await Should.ThrowAsync<RequestValidationException>(() => guest.Send(new RequestMyEmailChange(TestApp.GuestEmail), Ct).AsTask()))
            .Errors.ShouldContainKey("NewEmail");
    }

    [Fact]
    public async Task An_account_made_without_a_password_can_set_one_and_one_with_a_password_cannot()
    {
        var (guest, email) = await GuestAsync();
        await Should.ThrowAsync<BusinessRuleException>(() => guest.Send(new SetMyPassword("newpass", "newpass"), Ct).AsTask());
        await using (var scope = TestBookings.TenantScope(app))
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await users.RemovePasswordAsync((await users.FindByEmailAsync(email))!)).Succeeded.ShouldBeTrue();
        }
        (await guest.Send(new GetMyProfile(), Ct)).HasPassword.ShouldBeFalse();

        await guest.Send(new SetMyPassword("newpass", "newpass"), Ct);

        (await guest.Send(new GetMyProfile(), Ct)).HasPassword.ShouldBeTrue();
        var response = await app.Anonymous().PostAsJsonAsync(AuthRoutes.SignIn, new SignInRequest { Email = email, Password = "newpass" }, Ct);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_guest_downloads_their_profile_and_bookings()
    {
        var (guest, email) = await GuestAsync();
        var id = await BookAsync(guest, email);

        var data = await guest.Send(new GetMyData(), Ct);

        data.Profile.Email.ShouldBe(email);
        data.Profile.Nationality.ShouldBe("Australia");
        data.Bookings.ShouldHaveSingleItem().Id.ShouldBe(id);
    }

    [GeneratedRegex("href=\"([^\"]*confirm-email[^\"]*)\"")]
    private static partial Regex Link();
}
