using System.Linq.Expressions;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Fuse.Application.Common.Jobs;

namespace Fuse.Infrastructure.Jobs;

/// <summary>
/// Hangfire, in its own schema (Hangfire:Schema, default "HangfireRebuild"): on the live database the "HangFire"
/// schema belongs to the legacy app, and sharing it would let each app's server run the other's jobs.
/// Every instance can queue jobs and show the dashboard; only some run them (<see cref="RunsJobs"/>), so a staging or
/// developer copy pointed at the live database never sends production's emails. Hangfire:Enabled is false only in
/// the tests, which supply their own <see cref="IBackgroundJobs"/>.
/// </summary>
public static class HangfireSetup
{
    public const string Section = "Hangfire";

    public static bool IsEnabled(IConfiguration configuration) => configuration.GetValue($"{Section}:Enabled", true);

    internal static void Add(IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        if (!IsEnabled(configuration))
            return;

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                SchemaName = configuration.GetValue($"{Section}:Schema", "HangfireRebuild"),
                PrepareSchemaIfNecessary = true,
                QueuePollInterval = TimeSpan.FromSeconds(15),
            })
            // Three tries over about ten minutes, as the old in-process queue, then it waits in Failed for a person.
            .UseFilter(new AutomaticRetryAttribute { Attempts = 3, DelaysInSeconds = [10, 60, 600] }));
        services.AddSingleton<IBackgroundJobs, HangfireJobs>();
    }

    /// <summary>
    /// Whether this instance runs jobs: Hangfire:Server when set; otherwise production, or a database on this
    /// machine (a developer's own copy). As legacy: only production works the live database's queue.
    /// </summary>
    public static bool RunsJobs(IConfiguration configuration, IHostEnvironment environment) =>
        IsEnabled(configuration)
        && (configuration.GetValue<bool?>($"{Section}:Server")
            ?? (environment.IsProduction() || Persistence.DatabaseSafety.IsLocal(configuration.GetConnectionString("Database") ?? "")));

    /// <summary>The job server, when <see cref="RunsJobs"/>.</summary>
    public static void AddServer(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (RunsJobs(configuration, environment))
            services.AddHangfireServer(options => options.WorkerCount = Math.Min(Environment.ProcessorCount * 2, 10));
    }
}

internal sealed class HangfireJobs(IBackgroundJobClient client) : IBackgroundJobs
{
    public void Enqueue<TService>(Expression<Func<TService, Task>> job) where TService : notnull => client.Enqueue(job);

    public void Schedule<TService>(Expression<Func<TService, Task>> job, TimeSpan delay) where TService : notnull => client.Schedule(job, delay);
}
