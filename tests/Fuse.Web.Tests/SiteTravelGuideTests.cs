using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Shouldly;
using Fuse.Contracts.Common;
using Fuse.Contracts.Site;
using Fuse.Contracts.TravelGuides;

namespace Fuse.Web.Tests;

/// <summary>The website's Blogs &amp; Tips: the paged list, a guide's page, and the old /travel-guides addresses.</summary>
public sealed class SiteTravelGuideTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private Fuse.Web.Client.Api.HttpApi Anonymous => new(app.Anonymous());

    private async Task<string> GuideAsync(string description, string subTitle = "Where to go out")
    {
        var api = await app.AdminApiAsync();
        var slug = $"guide-{Guid.NewGuid():N}";
        await api.Send(new CreateTravelGuide(new TravelGuideChanges
        {
            Details = new TravelGuideInput
            {
                Title = $"Guide {slug}", SubTitle = subTitle, ShortDescription = "<p>Short</p>", Description = description, UrlSlug = slug,
            },
            Banner = await api.Send(new UploadTravelGuideImage(new FileUpload("image.png", "image/png", [0x89, 0x50, 0x4E, 0x47, 1, 2, 3])), Ct),
        }), Ct);
        return slug;
    }

    [Fact]
    public async Task Guides_list_newest_first_seven_to_a_page_and_none_past_the_last()
    {
        await GuideAsync("<p>One</p>");
        await GuideAsync("<p>Two</p>");

        var first = await Anonymous.Send(new ListSiteTravelGuides(), Ct);

        first.Page.ShouldBe(1);
        first.Guides.Count.ShouldBeInRange(2, 7);
        first.Guides.Select(g => g.Published).ShouldBeInOrder(SortDirection.Descending);
        (await Anonymous.Send(new ListSiteTravelGuides(first.TotalPages + 1), Ct)).Guides.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_guide_lists_its_headings_for_the_table_of_contents_and_marks_them_in_the_text()
    {
        var slug = await GuideAsync("<h2 id=\"old\">Best Bars &amp; Clubs</h2><p>…</p><h3>Getting there</h3><h3>Getting there</h3><h6>Not listed</h6>");
        await GuideAsync("<p>Another</p>");

        var guide = await Anonymous.Send(new GetSiteTravelGuide(slug.ToUpperInvariant()), Ct);

        guide.Slug.ShouldBe(slug);
        guide.Contents.Select(c => (c.Id, c.Title)).ShouldBe(
        [
            ("whereToGoOut", "Where to go out"), ("bestBarsClubs", "Best Bars & Clubs"), ("gettingThere", "Getting there"), ("gettingThere-2", "Getting there"),
        ]);
        guide.Description.ShouldContain("<h2 id=\"bestBarsClubs\">Best Bars &amp; Clubs</h2>");
        guide.Description.ShouldContain("<h3 id=\"gettingThere-2\">");
        guide.Description.ShouldNotContain("id=\"old\"");
        guide.Related.Count.ShouldBeInRange(1, 3);
        guide.Related.ShouldNotContain(r => r.Slug == slug);
    }

    [Fact]
    public async Task An_unknown_guide_is_not_found()
    {
        await Should.ThrowAsync<NotFoundException>(() => Anonymous.Send(new GetSiteTravelGuide($"missing-{Guid.NewGuid():N}"), Ct).AsTask());
    }

    [Theory]
    [InlineData("/travel-guides", "/blogs-and-tips")]
    [InlineData("/travel-guides/best-hostel-hoi-an", "/blogs-and-tips/best-hostel-hoi-an")]
    [InlineData("/get-in-touch", "/contact")]
    public async Task Addresses_legacy_listed_but_never_served_move_permanently(string old, string current)
    {
        var response = await app.Anonymous().GetAsync(old, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.OriginalString.ShouldBe(current);
    }

    [Fact]
    public async Task The_pages_are_served_and_an_unknown_guide_is_a_404()
    {
        var slug = await GuideAsync("<h2>Hello</h2>");

        (await app.Anonymous().GetAsync("/blogs-and-tips", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await app.Anonymous().GetStringAsync($"/blogs-and-tips/{slug}", Ct);
        page.ShouldContain("id=\"hello\"");
        page.ShouldContain("\"@type\":\"BlogPosting\"");
        (await app.Anonymous().GetAsync($"/blogs-and-tips/missing-{Guid.NewGuid():N}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await app.Anonymous().GetAsync("/blogs-and-tips/9999", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_page_management_redirect_wins_over_the_moved_travel_guides_section()
    {
        var old = $"travel-guides/old-{Guid.NewGuid():N}";
        await using (var scope = TestBookings.TenantScope(app))
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.AppDbContext>();
            db.SeoRedirects.Add(new Domain.Catalog.SEO.SeoRedirect($"/{old}", "/blogs-and-tips/elsewhere"));
            await db.SaveChangesAsync(Ct);
        }

        (await app.Anonymous().GetAsync($"/{old}", Ct)).Headers.Location!.OriginalString.ShouldBe("/blogs-and-tips/elsewhere");
    }
}
