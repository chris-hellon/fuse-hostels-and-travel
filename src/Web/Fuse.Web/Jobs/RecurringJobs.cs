using Finbuckle.MultiTenant.Abstractions;
using Hangfire;
using Mediator;
using Fuse.Application.Common.Jobs;
using Fuse.Application.Common.Payments;
using Fuse.Application.Features.Dashboard;
using Fuse.Application.Features.Identity;
using Fuse.Application.Features.Manifest;
using Fuse.Application.Features.Site;
using Fuse.Contracts.Bookings;
using Fuse.Infrastructure.Multitenancy;
using Fuse.Shared.Multitenancy;

namespace Fuse.Web.Jobs;

/// <summary>
/// The scheduled jobs, on Hangfire (see /admin/jobs). Each can be switched on or off with Jobs:{name}. Legacy runs its
/// own manifest job on the live database, so this one is off until legacy's is: otherwise hostels get two manifests.
/// (Its Cloudbeds import can run alongside this one: both match guests already here before adding any.)
/// </summary>
public sealed class RecurringJobs(IServiceScopeFactory scopes, IBackgroundJobs jobs, ILogger<RecurringJobs> logger)
{
    private static readonly TimeZoneInfo Vietnam = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    private sealed record Job(string Id, string Setting, string Cron, Func<IHostEnvironment, bool> OnByDefault, System.Linq.Expressions.Expression<Func<RecurringJobs, Task>> Run);

    private static readonly Job[] Jobs =
    [
        // Legacy "Tours Daily Manifest": at midnight, schedules each of today's departures' manifests for 1 hour before
        // it leaves. (This app replaces legacy's, so only one ever runs.)
        new("tour-manifest-emails", "TourManifests", "0 0 * * *", _ => true, j => j.ScheduleTourManifestsAsync(CancellationToken.None)),
        // Legacy "Cloudbeds Guest Import": every 30 minutes, at :00 and :30.
        new("cloudbeds-guest-import", "CloudbedsImport", "0,30 * * * *", _ => true, j => j.ImportCloudbedsGuestsAsync(CancellationToken.None)),
    ];

    /// <summary>Whether the job is switched on (Jobs:{setting}, else its default).</summary>
    public static bool IsOn(string id, IConfiguration configuration, IHostEnvironment environment) =>
        Jobs.Single(j => j.Id == id) is var job && (configuration.GetValue<bool?>($"Jobs:{job.Setting}") ?? job.OnByDefault(environment));

    /// <summary>Adds (or updates) the switched-on jobs and removes the switched-off ones.</summary>
    public static void Register(IRecurringJobManager manager, IConfiguration configuration, IHostEnvironment environment)
    {
        foreach (var job in Jobs)
        {
            if (IsOn(job.Id, configuration, environment))
                manager.AddOrUpdate(job.Id, job.Run, job.Cron, new RecurringJobOptions { TimeZone = Vietnam });
            else
                manager.RemoveIfExists(job.Id);
        }
        // Retired: as legacy, unpaid website bookings stay Unpaid for staff to delete (it was registered by earlier builds).
        manager.RemoveIfExists("abandoned-checkouts");
        // Retired: the dashboard's Tour Attach Rate (registered by earlier builds).
        manager.RemoveIfExists("tour-attach-rate");
    }

    [DisableConcurrentExecution(timeoutInSeconds: 10 * 60), AutomaticRetry(Attempts = 0)]
    public Task ScheduleTourManifestsAsync(CancellationToken cancellationToken) =>
        InFuseAsync<TourManifestEmails>(async manifests =>
        {
            var departures = await manifests.TodaysAsync(cancellationToken);
            foreach (var (departure, delay) in departures)
                jobs.Schedule<RecurringJobs>(j => j.SendTourManifestAsync(departure, CancellationToken.None), delay);
            logger.LogInformation("Scheduled {Count} tour manifests for today", departures.Count);
        });

    /// <summary>One of <see cref="ScheduleTourManifestsAsync"/>'s departures, at its time.</summary>
    public Task SendTourManifestAsync(TourManifestDeparture departure, CancellationToken cancellationToken) =>
        InFuseAsync<TourManifestEmails>(manifests => manifests.SendAsync(departure, cancellationToken));

    [DisableConcurrentExecution(timeoutInSeconds: 30 * 60), AutomaticRetry(Attempts = 0)]
    public Task ImportCloudbedsGuestsAsync(CancellationToken cancellationToken) =>
        InFuseAsync<CloudbedsGuestImport>(import => import.ImportAsync(cancellationToken));

    /// <summary>Runs in a scope for the one site (outside a request there's no tenant to resolve).</summary>
    private async Task InFuseAsync<TService>(Func<TService, Task> run) where TService : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>().MultiTenantContext = new MultiTenantContext<FuseTenantInfo>(
            new FuseTenantInfo { Id = MultitenancyConstants.Fuse.Id, Identifier = MultitenancyConstants.Fuse.Identifier });
        await run(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
