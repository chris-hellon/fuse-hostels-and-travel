using Hangfire;
using Hangfire.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Fuse.Application.Common.Cloudbeds;
using Fuse.Application.Common.Mail;
using Fuse.Application.Features.Identity;
using Fuse.Domain.Identity;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Authorization;
using Fuse.Web.Jobs;

namespace Fuse.Web.Tests;

/// <summary>The Hangfire jobs: which run where, what they store, and the Cloudbeds guest import.</summary>
public sealed class BackgroundJobTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeCloudbeds Cloudbeds => (FakeCloudbeds)app.Services.GetRequiredService<ICloudbedsClient>();

    /// <summary>A hostel connected to Cloudbeds as <paramref name="cloudbedsId"/>.</summary>
    private async Task HostelAsync(string cloudbedsId)
    {
        await using var scope = TestBookings.TenantScope(app);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync("""
            DECLARE @now datetime2 = SYSUTCDATETIME(), @empty uniqueidentifier = '00000000-0000-0000-0000-000000000000';
            INSERT INTO Catalog.Properties (Id, Name, Description, AddressLine1, TelephoneNumber, CloudbedsPropertyId, CloudbedsApiKey, TenantId, CreatedBy, CreatedOn, LastModifiedBy)
            VALUES (NEWID(), 'Cloudbeds Hostel', 'A hostel', '1 Test Street', '000', {0}, 'key', 'fuse', @empty, @now, @empty);
            """, new object[] { cloudbedsId }, Ct);
    }

    private async Task<CloudbedsGuestImport.Result> ImportAsync()
    {
        await using var scope = TestBookings.TenantScope(app);
        return await scope.ServiceProvider.GetRequiredService<CloudbedsGuestImport>().ImportAsync(Ct);
    }

    private async Task<(ApplicationUser User, IList<string> Roles)?> UserAsync(Func<ApplicationUser, bool> match)
    {
        await using var scope = TestBookings.TenantScope(app);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = users.Users.AsEnumerable().FirstOrDefault(match);
        return user is null ? null : (user, await users.GetRolesAsync(user));
    }

    [Fact]
    public async Task The_Cloudbeds_import_adds_new_guests_and_fills_in_known_ones_without_overwriting_them()
    {
        var run = Guid.NewGuid().ToString("N")[..10];
        var property = $"cb-{run}";
        await HostelAsync(property);
        await HostelAsync("cb-down"); // can't be reached: skipped, not the whole run

        // Someone already here: matched by email and name, keeps their own phone.
        var knownEmail = $"known-{run}@tests.invalid";
        await (await app.AdminApiAsync()).Send(new Fuse.Contracts.Identity.CreateGuest(new Fuse.Contracts.Identity.UserProfile
        {
            FirstName = "Kate", LastName = "Known", Email = knownEmail, PhoneNumber = "0905 111",
        }), Ct);
        var sharedEmail = $"shared-{run}@tests.invalid";
        await (await app.AdminApiAsync()).Send(new Fuse.Contracts.Identity.CreateGuest(new Fuse.Contracts.Identity.UserProfile
        {
            FirstName = "Pat", LastName = "Partner", Email = sharedEmail,
        }), Ct);

        var newGuest = new CloudbedsGuest { GuestId = $"n{run}", FirstName = "Nina [2 beds]", LastName = "New", Email = $"new-{run}@tests.invalid", Country = "GB", Gender = "F" };
        Cloudbeds.Lists[property] = new()
        {
            ["in_house"] =
            [
                newGuest,
                new CloudbedsGuest
                {
                    GuestId = $"k{run}", FirstName = "Kate", LastName = "Known", Email = knownEmail, Phone = "+84 999",
                    DocumentType = "Passport", DocumentNumber = "P123", DocumentIssuingCountry = "AU",
                },
                // Same email as someone else, different person: imported without it.
                new CloudbedsGuest { GuestId = $"s{run}", FirstName = "Sam", LastName = "Sharer", Email = sharedEmail },
            ],
            ["not_checked_in"] = [newGuest], // on both lists: imported once
        };

        var result = await ImportAsync();

        (result.Created, result.Failed).ShouldBe((2, 0));
        var created = (await UserAsync(u => u.CloudbedsGuestId == $"n{run}")).ShouldNotBeNull();
        (created.User.FirstName, created.User.LastName, created.User.Email, created.User.Nationality)
            .ShouldBe(("Nina", "New", $"new-{run}@tests.invalid", "United Kingdom"));
        created.User.FullName.ShouldBe("Nina New"); // kept in step with the names (PersonNames)
        created.Roles.ShouldBe([FuseRoles.Guest]);

        var known = (await UserAsync(u => u.Email == knownEmail)).ShouldNotBeNull().User;
        (known.CloudbedsGuestId, known.PhoneNumber, known.PassportNumber).ShouldBe(($"k{run}", "0905 111", "P123"));

        // As legacy, a shared email is kept (they don't sign in with it: that's the other account's username).
        var sharer = (await UserAsync(u => u.CloudbedsGuestId == $"s{run}")).ShouldNotBeNull().User;
        sharer.Email.ShouldBe(sharedEmail);
        sharer.UserName.ShouldEndWith("@travaloudguest.com");

        // The next run finds them all by their Cloudbeds id.
        (await ImportAsync()).Created.ShouldBe(0);
        Cloudbeds.Lists.TryRemove(property, out _);
    }

    [Fact]
    public async Task The_Cloudbeds_import_keeps_a_guests_latest_Cloudbeds_id_and_imports_messy_and_shared_emails_as_legacy()
    {
        var run = Guid.NewGuid().ToString("N")[..10];
        var property = $"cb-{run}";
        await HostelAsync(property);

        var knownEmail = $"known-{run}@tests.invalid";
        await (await app.AdminApiAsync()).Send(new Fuse.Contracts.Identity.CreateGuest(new Fuse.Contracts.Identity.UserProfile
        {
            FirstName = "Kim", LastName = "Known", Email = knownEmail,
        }), Ct);
        // An email legacy's import already gave two accounts (the stock user store throws on these).
        var sharedEmail = $"group-{run}@tests.invalid";
        await LegacyGuestAsync(sharedEmail);
        await LegacyGuestAsync(sharedEmail);

        Cloudbeds.Lists[property] = new()
        {
            ["in_house"] =
            [
                new CloudbedsGuest { GuestId = $"k1{run}", FirstName = "Kim", LastName = "Known", Email = knownEmail },
                new CloudbedsGuest { GuestId = $"m{run}", FirstName = "Max", LastName = "Messy", Email = " none " },
                new CloudbedsGuest { GuestId = $"g{run}", FirstName = "Gil", LastName = "Group", Email = sharedEmail },
                new CloudbedsGuest { GuestId = $"a{run}", FirstName = "Ann", LastName = "Apostrophe", Email = $"o'neil-{run}@tests.invalid" },
            ],
        };
        (await ImportAsync()).ShouldSatisfyAllConditions(r => r.Created.ShouldBe(3), r => r.Failed.ShouldBe(0));

        (await UserAsync(u => u.CloudbedsGuestId == $"m{run}")).ShouldNotBeNull().User.Email.ShouldBe("none");
        (await UserAsync(u => u.CloudbedsGuestId == $"g{run}")).ShouldNotBeNull().User.Email.ShouldBe(sharedEmail);
        var apostrophe = (await UserAsync(u => u.CloudbedsGuestId == $"a{run}")).ShouldNotBeNull().User;
        (apostrophe.Email, apostrophe.UserName!.EndsWith("@travaloudguest.com")).ShouldBe(($"o'neil-{run}@tests.invalid", true));
        (await UserAsync(u => u.Email == knownEmail)).ShouldNotBeNull().User.CloudbedsGuestId.ShouldBe($"k1{run}");

        // A later stay has a new Cloudbeds id: it replaces the old one, as legacy.
        Cloudbeds.Lists[property] = new()
        {
            ["in_house"] = [new CloudbedsGuest { GuestId = $"k2{run}", FirstName = "Kim", LastName = "Known", Email = knownEmail }],
        };
        (await ImportAsync()).ShouldSatisfyAllConditions(r => r.Created.ShouldBe(0), r => r.Updated.ShouldBe(1));
        (await UserAsync(u => u.Email == knownEmail)).ShouldNotBeNull().User.CloudbedsGuestId.ShouldBe($"k2{run}");
        Cloudbeds.Lists.TryRemove(property, out _);
    }

    /// <summary>A guest written as legacy's import did: GUID username, straight to the table.</summary>
    private async Task LegacyGuestAsync(string email)
    {
        await using var scope = TestBookings.TenantScope(app);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = Guid.NewGuid().ToString(), Email = email, FirstName = "Lee", LastName = "Legacy", FullName = "Lee Legacy",
            IsActive = true, SecurityStamp = Guid.NewGuid().ToString(),
        };
        user.NormalizedUserName = users.NormalizeName(user.UserName);
        user.NormalizedEmail = users.NormalizeEmail(email);
        var roleId = await db.Roles.Where(r => r.Name == FuseRoles.Guest).Select(r => r.Id).SingleAsync(Ct);
        db.Users.Add(user);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roleId });
        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public void Queued_emails_survive_being_stored_by_Hangfire()
    {
        // Built as the app builds them ([to] makes a compiler list type Hangfire can't read back unless converted).
        var email = new EmailMessage(["a@tests.invalid"], "Manifest", "<p>Hi</p>")
        {
            Bcc = ["b@tests.invalid"], ReplyTo = "c@tests.invalid",
            Attachments = [new EmailAttachment("m.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [1, 2, 3])],
        };
        var jobs = new StoringJobs();
        GlobalConfiguration.Configuration.UseRecommendedSerializerSettings();

        jobs.EnqueueEmail(email);

        var stored = (EmailMessage)jobs.Stored.ShouldHaveSingleItem().Args[0];
        (stored.To.Single(), stored.Subject, stored.HtmlBody, stored.Bcc.Single(), stored.ReplyTo).ShouldBe(("a@tests.invalid", "Manifest", "<p>Hi</p>", "b@tests.invalid", "c@tests.invalid"));
        stored.Attachments.Single().ShouldSatisfyAllConditions(a => a.FileName.ShouldBe("m.xlsx"), a => a.Content.ShouldBe(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void The_tour_manifests_are_scheduled_once_switched_on()
    {
        var manager = new RecordingJobManager();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([new("Jobs:TourManifests", "true")]).Build();

        RecurringJobs.Register(manager, configuration, new Environment("Production"));

        manager.Added.ShouldContain("tour-manifest-emails");
    }

    [Fact]
    public void Scheduled_manifests_survive_being_stored_by_Hangfire()
    {
        var departure = new Fuse.Application.Features.Manifest.TourManifestDeparture(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Tour", new DateTime(2026, 9, 29, 8, 0, 0));
        var jobs = new StoringJobs();
        GlobalConfiguration.Configuration.UseRecommendedSerializerSettings();

        jobs.Schedule<RecurringJobs>(j => j.SendTourManifestAsync(departure, CancellationToken.None), TimeSpan.FromHours(1));

        jobs.Stored.ShouldHaveSingleItem().Args[0].ShouldBe(departure);
    }

    /// <summary>Stores each job as Hangfire does (its invocation data) and reads it back.</summary>
    private sealed class StoringJobs : Fuse.Application.Common.Jobs.IBackgroundJobs
    {
        public List<Job> Stored { get; } = [];

        public void Enqueue<TService>(System.Linq.Expressions.Expression<Func<TService, Task>> job) where TService : notnull =>
            Stored.Add(Hangfire.Storage.InvocationData.SerializeJob(Job.FromExpression(job)).DeserializeJob());

        public void Schedule<TService>(System.Linq.Expressions.Expression<Func<TService, Task>> job, TimeSpan delay) where TService : notnull =>
            Enqueue(job);
    }

    [Theory]
    [InlineData("Development", null, true)]
    [InlineData("Production", null, true)]
    [InlineData("Production", "false", false)]
    public void Scheduled_jobs_run_everywhere_unless_switched_off(string environment, string? setting, bool scheduled)
    {
        var manager = new RecordingJobManager();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(setting is null ? [] : [new("Jobs:CloudbedsImport", setting)])
            .Build();

        RecurringJobs.Register(manager, configuration, new Environment(environment));

        manager.Added.Contains("cloudbeds-guest-import").ShouldBe(scheduled);
        manager.Removed.Contains("cloudbeds-guest-import").ShouldBe(!scheduled);
        manager.Added.ShouldContain("tour-manifest-emails");
        manager.Added.ShouldNotContain("abandoned-checkouts"); // retired: unpaid website bookings stay for staff, as legacy
        manager.Removed.ShouldContain("abandoned-checkouts");
    }

    [Theory]
    [InlineData("Production", "Server=live.database.windows.net;Database=travaloud", null, true)]
    [InlineData("Staging", "Server=live.database.windows.net;Database=travaloud", null, false)] // never works live's queue
    [InlineData("Development", "Server=localhost;Database=travaloud", null, true)]
    [InlineData("Development", "Server=localhost;Database=travaloud", "false", false)]
    public void Only_production_or_a_local_database_runs_jobs(string environment, string database, string? setting, bool runs)
    {
        var values = new Dictionary<string, string?> { ["ConnectionStrings:Database"] = database };
        if (setting is not null)
            values["Hangfire:Server"] = setting;

        Infrastructure.Jobs.HangfireSetup.RunsJobs(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new Environment(environment))
            .ShouldBe(runs);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Fuse.Web";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class RecordingJobManager : IRecurringJobManager
    {
        public List<string> Added { get; } = [];
        public List<string> Removed { get; } = [];
        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options) => Added.Add(recurringJobId);
        public void Trigger(string recurringJobId) { }
        public void RemoveIfExists(string recurringJobId) => Removed.Add(recurringJobId);
    }
}
