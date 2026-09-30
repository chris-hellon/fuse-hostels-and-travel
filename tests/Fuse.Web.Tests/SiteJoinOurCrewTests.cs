using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Contracts.Common;
using Fuse.Contracts.JobVacancies;
using Fuse.Contracts.Site;
using Fuse.Infrastructure.Persistence;

namespace Fuse.Web.Tests;

/// <summary>The website's Join Our Crew: the open positions, applying for one, and the legal pages beside it.</summary>
public sealed class SiteJoinOurCrewTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private Fuse.Web.Client.Api.HttpApi Anonymous => new(app.Anonymous());

    private async Task<DefaultIdType> VacancyAsync(string? callToAction = null)
    {
        var api = await app.AdminApiAsync();
        return await api.Send(new SaveJobVacancy(null, new JobVacancyInput
        {
            JobTitle = $"Bartender {Guid.NewGuid():N}", Location = "Hoi An", Description = "<p>Pour drinks</p>", CallToAction = callToAction,
        }), Ct);
    }

    private static ApplyForJobVacancy Application(DefaultIdType vacancy, string email, string captcha = FakeCaptcha.Pass) =>
        new(vacancy, "Sam", "Walker", email, "Behind the bar", "March to May", "Thailand, Laos", "@sam", null, null, null, null, captcha);

    [Fact]
    public async Task Open_positions_are_listed_and_deleted_ones_are_not()
    {
        var open = await VacancyAsync();
        var closed = await VacancyAsync();
        await (await app.AdminApiAsync()).Send(new DeleteJobVacancy(closed), Ct);

        var listed = await Anonymous.Send(new ListSiteJobVacancies(), Ct);

        listed.ShouldContain(v => v.Id == open && v.Location == "Hoi An" && v.ApplyUrl == null);
        listed.ShouldNotContain(v => v.Id == closed);
        await Should.ThrowAsync<NotFoundException>(() => Anonymous.Send(new GetSiteJobVacancy(closed), Ct).AsTask());
    }

    [Theory]
    [InlineData("jobs@fuse.example", "mailto:jobs@fuse.example")]
    [InlineData("www.workaway.info/fuse", "https://www.workaway.info/fuse")]
    [InlineData("https://apply.example/fuse", "https://apply.example/fuse")]
    [InlineData("Ask at reception", null)]
    public async Task A_call_to_action_says_where_to_apply(string callToAction, string? applyUrl)
    {
        var id = await VacancyAsync(callToAction);

        (await Anonymous.Send(new GetSiteJobVacancy(id), Ct)).ApplyUrl.ShouldBe(applyUrl);
    }

    [Fact]
    public async Task An_application_is_saved_and_emailed_to_the_team_who_reply_to_the_applicant()
    {
        var id = await VacancyAsync();
        var email = $"applicant-{Guid.NewGuid():N}@tests.invalid";

        await Anonymous.Send(Application(id, email), Ct);

        await using var scope = TestBookings.TenantScope(app);
        var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>().JobVacancyResponses.SingleAsync(r => r.Email == email, Ct);
        (saved.JobVacancyId, saved.Instagram, saved.TikTok).ShouldBe((id, "@sam", null));
        var sent = app.Emails.Sent.Single(m => m.ReplyTo == email);
        sent.Subject.ShouldBe("Job Application");
        sent.HtmlBody.ShouldContain("Behind the bar");
    }

    [Fact]
    public async Task An_application_needs_its_answers_and_the_captcha()
    {
        var id = await VacancyAsync();

        var invalid = await Should.ThrowAsync<RequestValidationException>(() =>
            Anonymous.Send(new ApplyForJobVacancy(id, "", "", "x", "", "", "", null, null, null, null, null, ""), Ct).AsTask());
        invalid.Errors.Keys.ShouldBe(["FirstName", "LastName", "Email", "HowCanWeCollaborate", "EstimatedDates", "DestinationsVisited", "Captcha"], ignoreOrder: true);
        (await Should.ThrowAsync<BusinessRuleException>(() => Anonymous.Send(Application(id, "a@tests.invalid", captcha: "robot"), Ct).AsTask()))
            .Message.ShouldBe(EnquiryMessages.Captcha);
    }

    [Fact]
    public async Task The_pages_are_served()
    {
        var id = await VacancyAsync();

        foreach (var page in new[] { "/join-our-crew", $"/join-our-crew/{id}", "/terms-and-conditions", "/privacy-policy", "/cookie-policy" })
            (await app.Anonymous().GetAsync(page, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, page);
        (await app.Anonymous().GetAsync($"/join-our-crew/{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await app.Anonymous().GetStringAsync($"/join-our-crew/{id}", Ct)).ShouldContain("Pour drinks");
    }
}
