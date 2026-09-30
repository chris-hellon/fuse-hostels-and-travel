using Hangfire;
using Hangfire.Dashboard;
using Fuse.Shared.Authorization;

namespace Fuse.Web.Jobs;

/// <summary>
/// Hangfire's dashboard at /admin/jobs (legacy /jobs): signed-in staff with the "View Hangfire" permission, instead of
/// legacy's shared username and password.
/// </summary>
public static class HangfireDashboard
{
    public const string Path = "/admin/jobs";

    public static void UseJobsDashboard(this WebApplication app) =>
        app.UseHangfireDashboard(Path, new DashboardOptions
        {
            Authorization = [new StaffPermission()],
            AppPath = "/admin",
            DashboardTitle = "Fuse Jobs",
            DisplayStorageConnectionString = false,
        });

    private sealed class StaffPermission : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context) =>
            context.GetHttpContext().User.HasPermission(FuseAction.View, FuseResource.Hangfire);
    }
}
