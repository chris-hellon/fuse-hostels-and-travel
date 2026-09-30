using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Contracts.Common;
using Fuse.Contracts.Galleries;
using Fuse.Contracts.JobVacancies;
using Fuse.Contracts.Pages;
using Fuse.Domain.Catalog.Pages;
using Fuse.Infrastructure.Persistence;

namespace Fuse.Web.Tests;

/// <summary>Job vacancies, galleries and page management.</summary>
public sealed class ManagementTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly FileUpload Png = new("image.png", "image/png", [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);

    private async Task<DefaultIdType> NewPageAsync(string title)
    {
        await using var scope = TestBookings.TenantScope(app);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var page = new Page(title, "old keywords", null, null, null, "slug-" + Guid.NewGuid().ToString("N")[..8], "Old heading", null, "Old third", null);
        db.Pages.Add(page);
        await db.SaveChangesAsync(Ct);
        return page.Id;
    }

    [Fact]
    public async Task A_job_vacancy_is_saved_edited_and_its_call_to_action_can_be_cleared()
    {
        var api = await app.AdminApiAsync();
        var title = $"Bartender {Guid.NewGuid():N}";
        var id = await api.Send(new SaveJobVacancy(null, new JobVacancyInput { JobTitle = title, Location = "Hoi An", CallToAction = "jobs@example.invalid", Description = "<p>Pour drinks</p>" }), Ct);

        var saved = await api.Send(new GetJobVacancy(id), Ct);
        saved.CallToAction.ShouldBe("jobs@example.invalid");
        await api.Send(new SaveJobVacancy(id, saved with { CallToAction = null, Location = "Nha Trang" }), Ct);
        var edited = await api.Send(new GetJobVacancy(id), Ct);
        (edited.CallToAction, edited.Location).ShouldBe((null, "Nha Trang"));

        (await api.Send(new SearchJobVacancies { Keyword = title }, Ct)).Items.ShouldHaveSingleItem().Responses.ShouldBe(0);
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SaveJobVacancy(null, new JobVacancyInput { Location = "Hoi An" }), Ct).AsTask());
        await api.Send(new DeleteJobVacancy(id), Ct);
        (await api.Send(new SearchJobVacancies { Keyword = title }, Ct)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_gallery_needs_an_image_and_keeps_its_images_in_order()
    {
        var api = await app.AdminApiAsync();
        var input = new GalleryInput { Title = $"Beach {Guid.NewGuid():N}", Description = "<p>Sun</p>", MetaKeywords = "beach" };
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SaveGallery(null, input, []), Ct).AsTask());

        var (a, b) = (await api.Send(new UploadGalleryImage(Png), Ct), await api.Send(new UploadGalleryImage(Png), Ct));
        var id = await api.Send(new SaveGallery(null, input, [new GalleryRef(null, a), new GalleryRef(null, b)]), Ct);
        var editor = await api.Send(new GetGalleryEditor(id), Ct);
        editor.Images.Select(i => i.ImagePath).ShouldBe([a, b]);

        var c = await api.Send(new UploadGalleryImage(Png), Ct);
        await api.Send(new SaveGallery(id, editor.Details, [new GalleryRef(editor.Images[1].Id, null), new GalleryRef(null, c)]), Ct);
        (await api.Send(new GetGalleryEditor(id), Ct)).Images.Select(i => i.ImagePath).ShouldBe([b, c]);
        (await api.Send(new SearchGalleries { Keyword = input.Title }, Ct)).Items.ShouldHaveSingleItem().Images.ShouldBe(2);
    }

    [Fact]
    public async Task Page_seo_is_edited_here_except_for_tour_and_property_pages()
    {
        var api = await app.AdminApiAsync();
        var aboutId = await NewPageAsync($"About {Guid.NewGuid():N}");
        var about = await api.Send(new GetPageEditor(aboutId), Ct);

        await api.Send(new UpdatePage(aboutId, about.Details with { H1 = "About us", MetaKeywords = null, H3 = "ignored" }), Ct);
        var saved = (await api.Send(new GetPageEditor(aboutId), Ct)).Details;
        (saved.H1, saved.MetaKeywords, saved.H3).ShouldBe(("About us", null, "Old third")); // only Home has a third heading

        var tourPageId = await NewPageAsync($"Tours - Boat {Guid.NewGuid():N}");
        await Should.ThrowAsync<BusinessRuleException>(() => api.Send(new UpdatePage(tourPageId, about.Details), Ct).AsTask());
        await Should.ThrowAsync<BusinessRuleException>(() => api.Send(new DeletePage(tourPageId), Ct).AsTask());
    }

    [Fact]
    public async Task A_page_modal_shows_on_chosen_pages_between_whole_days()
    {
        var api = await app.AdminApiAsync();
        var (home, tours) = (await NewPageAsync($"Home-ish {Guid.NewGuid():N}"), await NewPageAsync($"Tours-ish {Guid.NewGuid():N}"));
        var input = new PageModalInput { Title = $"Sale {Guid.NewGuid():N}", StartDate = new DateTime(2030, 1, 1), EndDate = new DateTime(2030, 1, 7), PageIds = [home, tours] };
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SavePageModal(null, input with { PageIds = [] }), Ct).AsTask());
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SavePageModal(null, input with { EndDate = null }), Ct).AsTask());

        var id = await api.Send(new SavePageModal(null, input), Ct);
        var row = (await api.Send(new SearchPageModals { Keyword = input.Title }, Ct)).Items.ShouldHaveSingleItem();
        (row.StartDate, row.EndDate, row.Pages).ShouldBe((new DateTime(2030, 1, 1), new DateTime(2030, 1, 7, 23, 59, 0), 2));

        await api.Send(new SavePageModal(id, (await api.Send(new GetPageModal(id), Ct)) with { PageIds = [tours], StartDate = null, EndDate = null }), Ct);
        var edited = await api.Send(new GetPageModal(id), Ct);
        edited.PageIds.ShouldBe([tours]);
        edited.StartDate.ShouldBeNull();
    }

    [Fact]
    public async Task Global_seo_saves_and_clears_and_redirects_are_slugs_used_once()
    {
        var api = await app.AdminApiAsync();
        await api.Send(new SaveGlobalSeo(new GlobalSeo { Breadcrumbs = "<script>1</script>", SearchAction = "<script>2</script>" }), Ct);
        await api.Send(new SaveGlobalSeo(new GlobalSeo { Breadcrumbs = "<script>1</script>", SearchAction = "" }), Ct);
        var seo = await api.Send(new GetGlobalSeo(), Ct);
        (seo.Breadcrumbs, seo.SearchAction).ShouldBe(("<script>1</script>", ""));

        var from = $"old-{Guid.NewGuid():N}";
        var id = await api.Send(new SavePageRedirect(null, $" /{from} ", "/new-page"), Ct);
        (await api.Send(new SearchPageRedirects { Keyword = from }, Ct)).Items.ShouldHaveSingleItem().Url.ShouldBe($"/{from}"); // as entered, trimmed
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SavePageRedirect(null, $"/{from}", "/elsewhere"), Ct).AsTask());
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SavePageRedirect(null, "https://example.com/x", "y"), Ct).AsTask());
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SavePageRedirect(null, "same", "same"), Ct).AsTask());
        await api.Send(new SavePageRedirect(id, $"/{from}", "/newer-page"), Ct); // editing keeps its own URL
        await api.Send(new DeletePageRedirect(id), Ct);
    }
}
