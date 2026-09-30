using Shouldly;
using Fuse.Contracts.Api;
using Fuse.Contracts.Common;
using Fuse.Contracts.TravelGuides;
using Fuse.Shared.Text;

namespace Fuse.Web.Tests;

public sealed class TravelGuideTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly FileUpload Png = new("image.png", "image/png", [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);

    private async Task<(IApi Api, TravelGuideChanges Changes)> NewGuideAsync()
    {
        var api = await app.AdminApiAsync();
        var title = $"Best Bars & Clubs {Guid.NewGuid():N}";
        return (api, new TravelGuideChanges
        {
            Details = new TravelGuideInput
            {
                Title = title, SubTitle = "Where to go out", ShortDescription = "<p>Nights out</p>", Description = "<p>The guide</p>",
                MetaKeywords = "bars, clubs",
            },
            Banner = await api.Send(new UploadTravelGuideImage(Png), Ct),
            Gallery = [new GalleryRef(null, await api.Send(new UploadTravelGuideImage(Png), Ct)), new GalleryRef(null, await api.Send(new UploadTravelGuideImage(Png), Ct))],
        });
    }

    [Fact]
    public async Task A_guide_is_saved_with_its_banner_slideshow_and_a_slug_from_the_title()
    {
        var (api, changes) = await NewGuideAsync();

        var id = await api.Send(new CreateTravelGuide(changes), Ct);
        var editor = await api.Send(new GetTravelGuideEditor(id), Ct);

        editor.Details.UrlSlug.ShouldBe(Slugs.From(changes.Details.Title));
        editor.Details.UrlSlug.ShouldStartWith("best-bars-and-clubs-");
        editor.ImagePath.ShouldBe(changes.Banner);
        editor.Gallery.Select(g => g.ImagePath).ShouldBe(changes.Gallery.Select(g => g.Url!));
        (await api.Send(new SearchTravelGuides { Keyword = changes.Details.Title }, Ct)).Items.ShouldHaveSingleItem().UrlSlug.ShouldBe(editor.Details.UrlSlug);
    }

    [Fact]
    public async Task Editing_reorders_removes_and_adds_slideshow_images()
    {
        var (api, changes) = await NewGuideAsync();
        var id = await api.Send(new CreateTravelGuide(changes), Ct);
        var editor = await api.Send(new GetTravelGuideEditor(id), Ct);
        var added = await api.Send(new UploadTravelGuideImage(Png), Ct);
        var banner = await api.Send(new UploadTravelGuideImage(Png), Ct);

        await api.Send(new UpdateTravelGuide(id, new TravelGuideChanges
        {
            Details = editor.Details with { UrlSlug = "my-custom-slug-" + id.ToString("N")[..8] },
            Banner = banner,
            Gallery = [new GalleryRef(null, added), new GalleryRef(editor.Gallery[1].Id, null)],
        }), Ct);

        var saved = await api.Send(new GetTravelGuideEditor(id), Ct);
        saved.Gallery.Select(g => g.ImagePath).ShouldBe([added, editor.Gallery[1].ImagePath]);
        saved.ImagePath.ShouldBe(banner);
        saved.Details.UrlSlug.ShouldStartWith("my-custom-slug-");
    }

    [Fact]
    public async Task Titles_and_slugs_are_unique_and_the_guide_needs_its_text_and_a_banner()
    {
        var (api, changes) = await NewGuideAsync();
        await api.Send(new CreateTravelGuide(changes), Ct);
        var (_, other) = await NewGuideAsync();

        Task Refused(TravelGuideChanges c) => Should.ThrowAsync<RequestValidationException>(() => api.Send(new CreateTravelGuide(c), Ct).AsTask());

        await Refused(other with { Details = other.Details with { Title = changes.Details.Title } });
        await Refused(other with { Details = other.Details with { UrlSlug = Slugs.From(changes.Details.Title) } });
        await Refused(other with { Details = other.Details with { UrlSlug = "not a slug" } });
        await Refused(other with { Details = other.Details with { ShortDescription = "<p><br></p>" } });
        await Refused(other with { Banner = null });
        await Refused(other with { Banner = "https://example.invalid/fuse/assets/images/x.png" });
    }

    [Theory]
    [InlineData("Best Bars & Clubs in Nha Trang", "best-bars-and-clubs-in-nha-trang")]
    [InlineData("Chơi Gì Ở Nha Trang? Tổng hợp 10 Địa Điểm", "choi-gi-o-nha-trang-tong-hop-10-dia-diem")]
    [InlineData("  10 Best Things To Do In Hoi An [Updated 2025] ", "10-best-things-to-do-in-hoi-an-updated-2025")]
    [InlineData("MARBLE & MONKEY MOUNTAIN", "marble-and-monkey-mountain")]
    [InlineData("A&B Bar", "aandb-bar")]
    [InlineData("🇻🇳 INDEPENDENCE DAY – SUNSET RIVER CRUISE & BAR HOP", "independence-day-sunset-river-cruise-and-bar-hop")]
    [InlineData("Smørrebrød Łódź Straße Þór", "smorrebrod-lodz-strasse-thor")]
    [InlineData("a ж b", "a--b")] // legacy: a dropped letter still ends a run of dashes
    [InlineData("BUS (SLEEPER) HOI AN - QUY NHƠN", "bus-sleeper-hoi-an-quy-nhon")]
    public void Slugs_are_made_like_legacy(string title, string slug) => Slugs.From(title).ShouldBe(slug);
}
