using Shouldly;
using Fuse.Contracts.Common;
using Fuse.Contracts.Enquiries;
using Fuse.Contracts.JobVacancies;
using Fuse.Contracts.Site;

namespace Fuse.Web.Tests;

/// <summary>The admin's enquiry screens: what the website's forms sent, read, marked replied, exported and deleted.</summary>
public sealed class EnquiryAdminTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private Fuse.Web.Client.Api.HttpApi Site => new(app.Anonymous());

    [Fact]
    public async Task A_contact_enquiry_is_found_read_marked_replied_exported_and_deleted()
    {
        var api = await app.AdminApiAsync();
        var email = $"enq-{Guid.NewGuid():N}@tests.invalid";
        await Site.Send(new SendEnquiry("Sam Walker", email, "+84 123", "Lockers?", null, null, FakeCaptcha.Pass), Ct);

        var found = (await api.Send(new SearchGeneralEnquiries { Keyword = email }, Ct)).Items.ShouldHaveSingleItem();
        found.Replied.ShouldBeFalse();
        found.Submitted.ShouldBe(TestBookings.BusinessNow, TimeSpan.FromMinutes(5)); // shown in Vietnam time
        (await api.Send(new GetGeneralEnquiry(found.Id), Ct)).Message.ShouldBe("Lockers?");

        await api.Send(new SetEnquiryReplied(EnquiryKind.General, found.Id, true), Ct);
        (await api.Send(new SearchGeneralEnquiries { Keyword = email, Replied = true }, Ct)).Items.ShouldHaveSingleItem();
        (await api.Send(new SearchGeneralEnquiries { Keyword = email, Replied = false }, Ct)).Items.ShouldBeEmpty();

        var file = await api.Send(new ExportEnquiries(EnquiryKind.General, email, null), Ct);
        file.FileName.ShouldEndWith(".xlsx");
        file.Content.Length.ShouldBeGreaterThan(0);

        await api.Send(new DeleteEnquiry(EnquiryKind.General, found.Id), Ct);
        (await api.Send(new SearchGeneralEnquiries { Keyword = email }, Ct)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_tour_enquiry_shows_its_tour_even_once_the_tour_is_deleted()
    {
        var api = await app.AdminApiAsync();
        var (tourId, _) = await TestBookings.TourWithDepartureAsync(app, api, TestBookings.BusinessNow.Date.AddDays(6).AddHours(9), Ct);
        await using (var scope = TestBookings.TenantScope(app))
            await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Infrastructure.Persistence.AppDbContext>(scope.ServiceProvider).Database,
                "UPDATE Catalog.Tours SET PublishToSite = 1 WHERE Id = {0}", new object[] { tourId }, Ct);
        var email = $"tenq-{Guid.NewGuid():N}@tests.invalid";
        await Site.Send(new SendTourEnquiry(tourId, "Sam", email, "+84", DateTime.Today.AddDays(9), 4, "Private", FakeCaptcha.Pass), Ct);
        await api.Send(new Fuse.Contracts.Tours.DeleteTour(tourId), Ct);

        var found = (await api.Send(new SearchTourEnquiries { Keyword = email }, Ct)).Items.ShouldHaveSingleItem();
        found.TourName.ShouldStartWith("Dash tour");
        found.NumberOfPeople.ShouldBe(4);
        (await api.Send(new GetTourEnquiry(found.Id), Ct)).AdditionalInformation.ShouldBe("Private");
        await api.Send(new SetEnquiryReplied(EnquiryKind.Tour, found.Id, true), Ct);
        (await api.Send(new GetTourEnquiry(found.Id), Ct)).Summary.Replied.ShouldBeTrue();
    }

    [Fact]
    public async Task A_job_application_stays_listed_after_its_vacancy_is_deleted_and_cannot_be_marked_replied()
    {
        var api = await app.AdminApiAsync();
        var vacancy = await api.Send(new SaveJobVacancy(null, new JobVacancyInput { JobTitle = $"Barista {Guid.NewGuid():N}", Location = "Hoi An" }), Ct);
        var email = $"job-{Guid.NewGuid():N}@tests.invalid";
        await Site.Send(new ApplyForJobVacancy(vacancy, "Sam", "Walker", email, "Coffee", "May", "Laos", null, null, null, null, null, FakeCaptcha.Pass), Ct);
        await api.Send(new DeleteJobVacancy(vacancy), Ct);

        var found = (await api.Send(new SearchJobApplications { Keyword = email }, Ct)).Items.ShouldHaveSingleItem();
        (found.Name, found.Location).ShouldBe(("Sam Walker", "Hoi An"));
        found.JobTitle.ShouldStartWith("Barista");
        (await api.Send(new GetJobApplication(found.Id), Ct)).HowCanWeCollaborate.ShouldBe("Coffee");
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SetEnquiryReplied(EnquiryKind.JobApplication, found.Id, true), Ct).AsTask());
    }

    [Fact]
    public async Task Guests_cannot_read_enquiries()
    {
        await Should.ThrowAsync<AppException>(() => Site.Send(new SearchGeneralEnquiries(), Ct).AsTask());
    }
}
