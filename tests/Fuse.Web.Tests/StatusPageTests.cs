using System.Net;
using Shouldly;

namespace Fuse.Web.Tests;

/// <summary>The error and not-found pages: the site's look, rendered on the server.</summary>
public sealed class StatusPageTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_missing_page_says_so_in_the_sites_look()
    {
        // As a browser asks (no API marker, wants HTML).
        var browser = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        browser.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        var response = await browser.GetAsync("/no-such-page-at-all", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        html.ShouldContain("PAGE NOT FOUND");
        html.ShouldContain("_content/Fuse.Site/css/fuse"); // the site stylesheet (its name carries a fingerprint)
    }

    [Fact]
    public async Task The_error_page_renders_on_the_server_with_a_reference_to_quote()
    {
        var html = await app.Anonymous().GetStringAsync("/error", Ct);

        html.ShouldContain("SOMETHING WENT WRONG");
        html.ShouldContain("<code>");
        html.ShouldContain("href=\"/contact\"");
        // Server-only: no browser-app marker for this page, so nothing tries to route /error in the browser.
        html.ShouldNotContain("<!--Blazor:{\"type\":\"webassembly\"");
    }
}
