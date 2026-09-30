using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Contracts.Common;
using Fuse.Contracts.Site;
using Fuse.Infrastructure.Persistence;

namespace Fuse.Web.Tests;

/// <summary>The website's contact form and tour Enquire Now: bot checks, saved enquiries and the team's email.</summary>
public sealed class SiteEnquiryTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private Fuse.Web.Client.Api.HttpApi Anonymous => new(app.Anonymous());

    private static string NewEmail() => $"visitor-{Guid.NewGuid():N}@tests.invalid";

    private static SendEnquiry Enquiry(string email, string captcha = FakeCaptcha.Pass, string? website = null) =>
        new("Sam Walker", email, "+84 123", "Do you have lockers?\nAnd towels?", "Test Hostel", "Bamboo Boats", captcha, website);

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> read)
    {
        await using var scope = TestBookings.TenantScope(app);
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task A_contact_enquiry_is_saved_and_emailed_to_the_team_who_reply_to_the_visitor()
    {
        var email = NewEmail();

        await Anonymous.Send(Enquiry(email), Ct);

        var saved = await DbAsync(db => db.GeneralEnquiries.SingleAsync(e => e.Email == email, Ct));
        saved.Message.ShouldBe("Do you have lockers?\nAnd towels?");
        saved.ResponseSent.ShouldBeFalse();
        var sent = app.Emails.Sent.Single(m => m.ReplyTo == email);
        sent.To.ShouldBe(["team@tests.invalid"]);
        sent.Subject.ShouldBe("Website Enquiry");
        sent.HtmlBody.ShouldContain("Test Hostel");
        sent.HtmlBody.ShouldContain("Bamboo Boats");
    }

    [Fact]
    public async Task An_enquiry_without_a_passing_captcha_is_refused_and_nothing_is_kept()
    {
        var email = NewEmail();

        (await Should.ThrowAsync<BusinessRuleException>(() => Anonymous.Send(Enquiry(email, captcha: "robot"), Ct).AsTask()))
            .Message.ShouldBe(EnquiryMessages.Captcha);
        (await Should.ThrowAsync<RequestValidationException>(() => Anonymous.Send(Enquiry(email, captcha: ""), Ct).AsTask()))
            .Errors.ShouldContainKey("Captcha");

        (await DbAsync(db => db.GeneralEnquiries.AnyAsync(e => e.Email == email, Ct))).ShouldBeFalse();
        app.Emails.Sent.ShouldNotContain(m => m.ReplyTo == email);
    }

    [Fact]
    public async Task A_bot_that_fills_the_hidden_field_is_thanked_but_ignored()
    {
        var email = NewEmail();

        await Anonymous.Send(Enquiry(email, website: "http://spam.example"), Ct);

        (await DbAsync(db => db.GeneralEnquiries.AnyAsync(e => e.Email == email, Ct))).ShouldBeFalse();
        app.Emails.Sent.ShouldNotContain(m => m.ReplyTo == email);
    }

    [Fact]
    public async Task One_address_can_send_20_forms_per_10_minutes()
    {
        Fuse.Contracts.Api.RateLimitedAttribute.DefaultPer10Minutes.ShouldBe(20);

        // A host of its own with the default limit instead of the tests' raised one (and no other test shares it).
        await using var host = app.WithWebHostBuilder(b =>
            b.UseSetting("RateLimits:FormsPer10Minutes", Fuse.Contracts.Api.RateLimitedAttribute.DefaultPer10Minutes.ToString()));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(Fuse.Contracts.Api.ApiHeaders.Marker, "1");
        var route = $"/{Fuse.Contracts.Api.ContractMetadata.RoutePrefix}/site/enquiries";

        // Invalid forms are refused by validation, after the limiter has counted them: nothing is saved or emailed.
        for (var i = 0; i < 20; i++)
            (await client.PostAsJsonAsync(route, new { }, Ct)).StatusCode.ShouldNotBe(System.Net.HttpStatusCode.TooManyRequests);

        (await client.PostAsJsonAsync(route, new { }, Ct)).StatusCode.ShouldBe(System.Net.HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Every_contact_field_is_required()
    {
        var failure = await Should.ThrowAsync<RequestValidationException>(() =>
            Anonymous.Send(new SendEnquiry("", "not-an-email", "", "", null, null, FakeCaptcha.Pass), Ct).AsTask());

        failure.Errors.Keys.ShouldBe(["Name", "Email", "ContactNumber", "Message"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_tour_enquiry_is_saved_against_the_tour_and_emailed_with_its_name()
    {
        var api = await app.AdminApiAsync();
        var (tourId, _) = await TestBookings.TourWithDepartureAsync(app, api, TestBookings.BusinessNow.Date.AddDays(4).AddHours(9), Ct);
        await DbAsync(db => db.Database.ExecuteSqlRawAsync("UPDATE Catalog.Tours SET PublishToSite = 1 WHERE Id = {0}", [tourId], Ct));
        var tourName = await DbAsync(db => db.Tours.Where(t => t.Id == tourId).Select(t => t.Name).SingleAsync(Ct));
        var email = NewEmail();
        var date = DateTime.Today.AddDays(10);

        await Anonymous.Send(new SendTourEnquiry(tourId, "Sam Walker", email, "+84 123", date, 6, "Private group", FakeCaptcha.Pass), Ct);

        var saved = await DbAsync(db => db.TourEnquiries.SingleAsync(e => e.Email == email, Ct));
        (saved.TourId, saved.RequestedDate, saved.NumberOfPeople, saved.AdditionalInformation).ShouldBe((tourId, date, 6, "Private group"));
        var sent = app.Emails.Sent.Single(m => m.ReplyTo == email);
        sent.Subject.ShouldBe("Tour Booking Enquiry");
        sent.HtmlBody.ShouldContain(tourName);
        sent.HtmlBody.ShouldContain(date.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)); // as legacy
    }

    [Fact]
    public async Task A_tour_enquiry_needs_a_published_tour_a_date_and_people()
    {
        var api = await app.AdminApiAsync();
        var (tourId, _) = await TestBookings.TourWithDepartureAsync(app, api, TestBookings.BusinessNow.Date.AddDays(4).AddHours(9), Ct);
        await DbAsync(db => db.Database.ExecuteSqlRawAsync("UPDATE Catalog.Tours SET PublishToSite = 0 WHERE Id = {0}", [tourId], Ct));

        await Should.ThrowAsync<NotFoundException>(() =>
            Anonymous.Send(new SendTourEnquiry(tourId, "Sam", NewEmail(), "+84", DateTime.Today.AddDays(3), 2, null, FakeCaptcha.Pass), Ct).AsTask());
        (await Should.ThrowAsync<RequestValidationException>(() =>
            Anonymous.Send(new SendTourEnquiry(tourId, "Sam", NewEmail(), "+84", null, null, null, FakeCaptcha.Pass), Ct).AsTask()))
            .Errors.Keys.ShouldBe(["Date", "People"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_site_hands_the_page_its_captcha_site_key()
    {
        var chrome = await Anonymous.Send(new GetSiteChrome(), Ct);

        chrome.CaptchaSiteKey.ShouldNotBeNullOrEmpty();
    }
}
