using System.Net;
using Shouldly;
using Fuse.Contracts.Api;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Fuse.Contracts.Locations;
using Fuse.Infrastructure.Multitenancy;
using Fuse.Shared.Multitenancy;

namespace Fuse.Web.Tests;

/// <summary>
/// Opening a page directly (a fresh tab, a refresh) prerenders it on the server, where every
/// component's data loads in-process at once. Browser navigation never exercises that path.
/// </summary>
public sealed class PrerenderTests(TestApp app)
{
    public static TheoryData<string> AdminPages => new(
        "/admin", "/admin/staff", "/admin/guests", "/admin/permissions", "/admin/account",
        "/admin/management/locations", "/admin/management/tours", "/admin/bookings/tour-bookings", "/admin/management/tours/tour-categories", "/admin/management/tours/packages", "/admin/management/tours/pack-incentives", "/admin/management/tours/suppliers", "/admin/management/properties", "/admin/management/events", "/admin/management/event-categories", "/admin/management/travel-guides", "/admin/calendar", "/admin/manifest", "/admin/management/job-vacancies", "/admin/management/galleries", "/admin/management/page-management/pages", "/admin/management/page-management/page-modals", "/admin/management/page-management/global-seo", "/admin/management/page-management/page-redirects",
        "/admin/enquiries/general-enquiries", "/admin/enquiries/tours-enquiries", "/admin/enquiries/job-vacancies");

    [Theory, MemberData(nameof(AdminPages))]
    public async Task Admin_pages_serve_a_loading_screen_and_render_once_in_the_browser(string path)
    {
        var admin = await app.SignedInAsync(TestApp.AdminEmail, staffOnly: true);
        var response = await admin.GetAsync(path, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldNotContain("<title>Error</title>");
        // Not prerendered (it rendered twice and flashed): the server sends the loading screen and the admin styles.
        html.ShouldContain("admin-boot");
        html.ShouldContain("_content/Fuse.Admin/admin");
    }

    [Fact]
    public async Task The_server_api_handles_parallel_calls()
    {
        // Prerendered pages load every component's data at once through the server IApi,
        // so each call needs its own DbContext (a shared one throws on concurrent use).
        // Outside a request the tenant is set by hand (async-local, so it flows into each call's scope).
        app.Services.GetRequiredService<IMultiTenantContextSetter>().MultiTenantContext = new MultiTenantContext<FuseTenantInfo>(
            new FuseTenantInfo { Id = MultitenancyConstants.Fuse.Id, Identifier = MultitenancyConstants.Fuse.Identifier });
        await using var scope = app.Services.CreateAsyncScope();
        var api = scope.ServiceProvider.GetRequiredService<IApi>();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.Send(new ListLocations(), TestContext.Current.CancellationToken).AsTask()));

        results.ShouldAllBe(r => r.Count > 0);
    }
}
