using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Contracts.Common;
using Fuse.Contracts.Site;
using Fuse.Infrastructure.Persistence;

namespace Fuse.Web.Tests;

/// <summary>The public website's home page and the data behind it.</summary>
public sealed class SiteTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_home_page_prerenders_for_visitors()
    {
        var response = await app.Anonymous().GetAsync("/", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("HOSTELS IN VIETNAM");
        html.ShouldContain("TAILORED TOURS");
        html.ShouldContain("_content/Fuse.Site/css/fuse");
        // Outside production search engines are told to stay away, and no analytics run.
        html.ShouldContain("noindex, nofollow");
        html.ShouldNotContain("googletagmanager");
    }

    [Theory]
    [InlineData("/our-story", "Our Story", "OUR CORE VALUES")]
    [InlineData("/hostels", "Hostels", "Search For Rooms")]
    [InlineData("/events", "Events", "STAY UP TO DATE")]
    public async Task Pages_prerender_with_their_title_and_breadcrumb(string path, string name, string content)
    {
        var response = await app.Anonymous().GetAsync(path, Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain($"<title>{name} - Fuse Hostels &amp; Travel</title>");
        html.ShouldContain(content);
        html.ShouldContain("BreadcrumbList");
    }

    [Fact]
    public async Task A_published_hostel_has_a_page_and_others_are_not_found()
    {
        // The seed hostel, published.
        await using (var scope = TestBookings.TenantScope(app))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync($"UPDATE Catalog.Properties SET PublishToSite = 1 WHERE Name = '{TestApp.SeedPropertyName}'", Ct);
        }

        var anonymous = new Fuse.Web.Client.Api.HttpApi(app.Anonymous());
        var seed = (await anonymous.Send(new ListSiteHostels(), Ct)).Single(h => h.Name == TestApp.SeedPropertyName);
        seed.Slug.ShouldBe("test-hostel");
        await Should.ThrowAsync<NotFoundException>(() => anonymous.Send(new GetSiteHostel("no-such-hostel"), Ct).AsTask());
        (await app.Anonymous().GetAsync("/hostels/no-such-hostel", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var hostel = await anonymous.Send(new GetSiteHostel(seed.Slug), Ct);
        hostel.Id.ShouldBe(seed.Id);
        var response = await app.Anonymous().GetAsync($"/hostels/{hostel.Slug}", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("ACCOMMODATION");
        html.ShouldContain("Search For Rooms");
        html.ShouldContain("<title>Test Hostel - Fuse Hostels &amp; Travel</title>");
    }

    [Fact]
    public async Task The_events_feed_is_an_icalendar_of_published_events()
    {
        var response = await app.Anonymous().GetAsync("/events/calendar.ics", Ct);
        var ics = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/calendar");
        ics.ShouldStartWith("BEGIN:VCALENDAR\r\n");
        ics.ShouldEndWith("END:VCALENDAR\r\n");
    }

    [Fact]
    public async Task Booking_a_hostel_goes_to_its_cloudbeds_page_with_the_dates()
    {
        await using (var scope = TestBookings.TenantScope(app))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                $"UPDATE Catalog.Properties SET PublishToSite = 1, CloudbedsKey = NULL WHERE Name = '{TestApp.SeedPropertyName}'", Ct);
        }
        // No Cloudbeds key: nowhere to book.
        (await app.Anonymous().GetAsync("/hostel-booking/test-hostel?checkin=2026-10-01&checkout=2026-10-03", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await using (var scope = TestBookings.TenantScope(app))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                $"UPDATE Catalog.Properties SET CloudbedsKey = 'abc123' WHERE Name = '{TestApp.SeedPropertyName}'", Ct);
        }
        // Old links carried a user id; it's ignored.
        var response = await app.Anonymous().GetAsync("/hostel-booking/test-hostel/some-user-id?checkin=2026-10-01&checkout=2026-10-03", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldBe("https://hotels.cloudbeds.com/reservation/abc123#&checkin=2026-10-01&checkout=2026-10-03");
    }

    [Fact]
    public async Task Hostel_booking_links_find_the_hostel_by_its_slug_or_by_its_name_as_legacy_links_did()
    {
        await SetSeedHostelAsync("PublishToSite = 1, CloudbedsKey = 'abc123', UrlSlug = 'fuse-test-stay'");
        try
        {
            // Legacy made its links (and matched them) from the name; ours use the slug; both must still work.
            foreach (var segment in new[] { "fuse-test-stay", "test-hostel", "Test%20Hostel" })
            {
                var response = await app.Anonymous().GetAsync($"/hostel-booking/{segment}?checkin=2026-10-01&checkout=2026-10-03", Ct);
                response.StatusCode.ShouldBe(HttpStatusCode.Redirect, segment);
                response.Headers.Location!.ToString().ShouldBe("https://hotels.cloudbeds.com/reservation/abc123#&checkin=2026-10-01&checkout=2026-10-03");
            }
            (await app.Anonymous().GetAsync("/hostel-booking/no-such-hostel", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

            var anonymous = new Fuse.Web.Client.Api.HttpApi(app.Anonymous());
            (await anonymous.Send(new GetHostelBookingEngine("test-hostel"), Ct)).Slug.ShouldBe("fuse-test-stay");
        }
        finally
        {
            await SetSeedHostelAsync("UrlSlug = NULL");
        }
    }

    [Fact]
    public async Task A_hostel_page_takes_its_title_and_scripts_from_its_cms_page_and_its_meta_from_the_hostel()
    {
        await SetSeedHostelAsync("PublishToSite = 1, MetaKeywords = NULL, MetaDescription = 'Own description', CustomSeoScripts = '<script>/*own*/</script>'");
        await using (var scope = TestBookings.TenantScope(app))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Pages.Add(new Fuse.Domain.Catalog.Pages.Page($"Hostels - {TestApp.SeedPropertyName}", "Page keywords", "Page description", null,
                "<script>/*page*/</script>", null, null, null, null, "FUSE Test Hostels"));
            await db.SaveChangesAsync(Ct);
        }
        try
        {
            var anonymous = new Fuse.Web.Client.Api.HttpApi(app.Anonymous());
            var seo = (await anonymous.Send(new GetSiteHostel("test-hostel"), Ct)).Seo;
            seo.Title.ShouldBe("FUSE Test Hostels");
            seo.MetaDescription.ShouldBe("Own description");
            seo.MetaKeywords.ShouldBe(""); // legacy: the hostel's own, empty when it has none
            seo.CustomSeoScripts.ShouldBe("<script>/*page*/</script><script>/*own*/</script>");

            var html = await (await app.Anonymous().GetAsync("/hostels/test-hostel", Ct)).Content.ReadAsStringAsync(Ct);
            html.ShouldContain("<title>FUSE Test Hostels</title>");
            html.ShouldContain("<meta name=\"keywords\" content=\"\" />");
            html.ShouldContain("<meta name=\"description\" content=\"Own description\" />");
            html.ShouldContain("/*page*/");
            html.ShouldContain("/*own*/");
        }
        finally
        {
            await using var scope = TestBookings.TenantScope(app);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync($"DELETE FROM Catalog.Pages WHERE Title = 'Hostels - {TestApp.SeedPropertyName}'", Ct);
            await SetSeedHostelAsync("MetaDescription = NULL, CustomSeoScripts = NULL");
        }
    }

    [Theory]
    [InlineData("/hostels", "budget hostels, cheap hostels, backpackers hostels, Vietnam travel")]
    [InlineData("/our-story", "our story, hostel brand, backpacking, travel community")]
    public async Task Pages_carry_legacy_keywords(string path, string keywords)
    {
        var html = await (await app.Anonymous().GetAsync(path, Ct)).Content.ReadAsStringAsync(Ct);
        html.ShouldContain($"<meta name=\"keywords\" content=\"{keywords}\" />");
    }

    [Fact]
    public async Task The_site_default_meta_keeps_legacys_terms_in_a_search_snippet_length_description()
    {
        var seo = (await new Fuse.Web.Client.Api.HttpApi(app.Anonymous()).Send(new GetSiteChrome(), Ct)).Seo;

        seo.MetaDescription.ShouldNotBeNull().Length.ShouldBeLessThanOrEqualTo(165);
        seo.MetaDescription.ShouldContain("Hoi An Old Town & beachside");
        var keywords = seo.MetaKeywords.ShouldNotBeNull().Split(", ");
        keywords.ShouldBeUnique();
        keywords.ShouldContain("Hoi An Beachside");
        keywords.ShouldContain("Nha Trang Beachside");
    }

    private async Task SetSeedHostelAsync(string set)
    {
        await using var scope = TestBookings.TenantScope(app);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync($"UPDATE Catalog.Properties SET {set} WHERE Name = '{TestApp.SeedPropertyName}'", Ct);
    }

    [Fact]
    public async Task Visitors_only_see_published_hostels_tours_and_events()
    {
        var anonymous = new Fuse.Web.Client.Api.HttpApi(app.Anonymous());
        var chrome = await anonymous.Send(new GetSiteChrome(), Ct);
        var hostels = await anonymous.Send(new ListSiteHostels(), Ct);
        var tours = await anonymous.Send(new ListSiteTours(), Ct);
        var events = await anonymous.Send(new ListSiteEvents(), Ct);

        await using var scope = TestBookings.TenantScope(app);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hostelIds = chrome.Hostels.Select(h => h.Id).Concat(hostels.Select(h => h.Id)).ToList();
        var tourIds = chrome.Tours.Select(t => t.Id).Concat(tours.Select(t => t.Id)).ToList();
        var eventIds = events.Select(e => e.Id).ToList();

        (await db.Properties.IgnoreQueryFilters().Where(p => hostelIds.Contains(p.Id)).AllAsync(p => p.PublishToSite == true, Ct)).ShouldBeTrue();
        (await db.Tours.IgnoreQueryFilters().Where(t => tourIds.Contains(t.Id)).AllAsync(t => t.PublishToSite == true, Ct)).ShouldBeTrue();
        (await db.Events.IgnoreQueryFilters().Where(e => eventIds.Contains(e.Id)).AllAsync(e => e.PublishToSite != false, Ct)).ShouldBeTrue();
        chrome.BookableHostels.ShouldAllBe(h => chrome.Hostels.Any(p => p.Id == h.Id));
        chrome.Hostels.ShouldAllBe(h => h.Slug.Length > 0);
        chrome.Seo.Indexable.ShouldBeFalse();
    }

    [Fact]
    public async Task Robots_txt_keeps_crawlers_out_before_production_and_points_to_the_sitemap()
    {
        var robots = await app.Anonymous().GetStringAsync("/robots.txt", Ct);

        robots.ShouldContain("User-agent: *");
        robots.ShouldContain("Disallow: /"); // the tests don't run as Production
        robots.ShouldContain("/sitemap.xml");
    }

    [Fact]
    public async Task The_sitemap_lists_the_pages_legacy_listed_and_each_event()
    {
        var api = await app.AdminApiAsync();
        var name = $"Quiz Night {Guid.NewGuid():N}";
        await api.Send(new Fuse.Contracts.Events.SaveEvent(null, new Fuse.Contracts.Events.EventInput
        {
            Name = name, StartDate = DateTime.Today.AddDays(3).AddHours(19), PublishToSite = true, AllProperties = true,
        }, null, false), Ct);
        await using (var scope = TestBookings.TenantScope(app))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync($"UPDATE Catalog.Properties SET PublishToSite = 1 WHERE Name = '{TestApp.SeedPropertyName}'", Ct);
        }
        var response = await app.Anonymous().GetAsync("/sitemap.xml", Ct);
        var xml = System.Xml.Linq.XDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        System.Xml.Linq.XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locs = xml.Descendants(ns + "loc").Select(l => new Uri(l.Value).AbsolutePath).ToList();

        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/xml");
        locs.ShouldContain("/hostels/test-hostel");
        foreach (var page in new[] { "/", "/hostels", "/tours", "/our-story", "/events", "/contact", "/blogs-and-tips", "/join-our-crew", "/terms-and-conditions", "/cookie-policy" })
            locs.ShouldContain(page);
        xml.Descendants(ns + "lastmod").First().Value.ShouldEndWith("+07:00");
        locs.ShouldNotContain("/hostel-booking"); // not a page
        locs.ShouldContain($"/events/{Shared.Text.Slugs.From(name)}");
    }

    [Fact]
    public async Task Old_addresses_redirect_permanently_as_set_in_page_management()
    {
        var old = $"old-page-{Guid.NewGuid():N}";
        await using (var scope = TestBookings.TenantScope(app))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SeoRedirects.Add(new Domain.Catalog.SEO.SeoRedirect($"/{old}", "/hostels"));
            await db.SaveChangesAsync(Ct);
        }

        // Any case, with or without the leading slash in the record (as legacy).
        var response = await app.Anonymous().GetAsync($"/{old.ToUpperInvariant()}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.ToString().ShouldBe("/hostels");
    }

    [Fact]
    public async Task Search_engines_get_every_direction_and_room_description_and_a_lower_case_canonical()
    {
        await using (var scope = TestBookings.TenantScope(app))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync($"UPDATE Catalog.Properties SET PublishToSite = 1 WHERE Name = '{TestApp.SeedPropertyName}'", Ct);
        }

        var html = await app.Anonymous().GetStringAsync("/Hostels/Test-Hostel", Ct);

        html.ShouldContain("<link rel=\"canonical\" href=\"https://fusehostelsandtravel.com/hostels/test-hostel\"", Case.Insensitive);
        html.ShouldContain("<div hidden>"); // the rooms' descriptions
    }

    [Fact]
    public async Task An_event_has_its_own_page_and_an_unknown_one_goes_to_all_events()
    {
        var api = await app.AdminApiAsync();
        var name = $"Beer Pong {Guid.NewGuid():N}";
        await api.Send(new Fuse.Contracts.Events.SaveEvent(null, new Fuse.Contracts.Events.EventInput
        {
            Name = name,
            StartDate = DateTime.Today.AddDays(2).AddHours(20),
            PublishToSite = true,
            AllProperties = true,
            ShortDescription = "<p>Cups at the ready</p>",
        }, null, false), Ct);

        var response = await app.Anonymous().GetAsync($"/events/{Shared.Text.Slugs.From(name)}", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain($"<title>{name} - Fuse Hostels &amp; Travel</title>");
        html.ShouldContain("UPCOMING DATES");
        html.ShouldContain("ALL HOSTELS");
        html.ShouldContain("<meta name=\"description\" content=\"Cups at the ready\"");

        var missing = await app.Anonymous().GetAsync("/events/no-such-event", Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        missing.Headers.Location!.ToString().ShouldEndWith("/events");
    }

    [Fact]
    public async Task A_hostels_booking_page_embeds_its_cloudbeds_booking_engine()
    {
        await using (var scope = TestBookings.TenantScope(app))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                $"UPDATE Catalog.Properties SET PublishToSite = 1, CloudbedsKey = 'ab12cd' WHERE Name = '{TestApp.SeedPropertyName}'", Ct);
        }

        var html = await app.Anonymous().GetStringAsync("/hostels/test-hostel/book?checkin=2026-10-01&checkout=2026-10-03", Ct);

        html.ShouldContain("<cb-immersive-experience mode=\"standard\" property-code=\"ab12cd\"");
        (await app.Anonymous().GetAsync("/hostels/no-such-hostel/book", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
