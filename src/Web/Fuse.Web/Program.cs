using System.Threading.RateLimiting;
using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Mediator;
using Microsoft.AspNetCore.Components.WebAssembly.Services;
using MudBlazor.Services;
using Fuse.Application;
using Fuse.Contracts.Api;
using Fuse.Infrastructure;
using Fuse.Infrastructure.Persistence;
using Fuse.UI.Auth;
using Fuse.Web.Jobs;
using Fuse.Web.Api;
using Fuse.Web.Auth;
using Fuse.Web.Components;
using Fuse.Web.PublicApi;

var builder = WebApplication.CreateBuilder(args);

Fuse.Shared.Globalization.MonthAbbreviations.UseThreeLetterMonths();

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization(options => options.SerializeAllClaims = true);
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
Fuse.Infrastructure.Jobs.HangfireSetup.AddServer(builder.Services, builder.Configuration, builder.Environment);
builder.Services.AddScoped<RecurringJobs>();
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(ContractMetadata), typeof(Fuse.Application.DependencyInjection)];
    options.PipelineBehaviors =
    [
        typeof(Fuse.Application.Common.Behaviours.LoggingBehaviour<,>),
        typeof(Fuse.Application.Common.Behaviours.AuthorizationBehaviour<,>),
        typeof(Fuse.Application.Common.Behaviours.ValidationBehaviour<,>),
        typeof(Fuse.Application.Common.Behaviours.SiteCacheBehaviour<,>),
    ];
});

// What components get during prerendering; the WebAssembly client registers HTTP versions.
builder.Services.AddScoped<IApi, MediatorApi>();
builder.Services.AddScoped<Fuse.UI.FileSaver>();
builder.Services.AddScoped<IAuthClient, ServerAuthClient>();
builder.Services.AddScoped<LazyAssemblyLoader>();
builder.Services.AddMudServices();

builder.Services.AddContractEndpointAuthorization();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.ConfigureHttpJsonOptions(options => ApiJson.Configure(options.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddPublicApi(builder.Configuration);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthEndpoints.RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimits:AuthPerMinute", 10),
            Window = TimeSpan.FromMinutes(1),
        }));
    options.AddPolicy(RateLimitedAttribute.Policy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimits:FormsPer10Minutes", RateLimitedAttribute.DefaultPer10Minutes),
            Window = TimeSpan.FromMinutes(10),
        }));
});

// The "ngrok" launch profile: print the tunnel's public URL (the app only knows localhost).
if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Ngrok:ShowUrl"))
    builder.Services.AddHostedService<Fuse.Web.Dev.NgrokUrl>();

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
    await DatabaseMigrator.MigrateAndSeedAsync(app.Services);

app.UseExceptionHandler("/error", createScopeForErrors: true);
app.UsePublicApiDocs();
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    // The admin's own endpoints (development only), clear of the public API's /api/openapi/v1.json.
    app.MapOpenApi("/api/app/openapi/{documentName}.json");
}
else
{
    app.UseHsts();
}

// Friendly pages for browser navigations only; API callers (and Stripe, at legacy's /webhooks) need the real status code and body.
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/webhooks"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();
// Routing after the status code pages, so a 404's re-executed /not-found is routed to its page (routed only at the
// start, it found no page and answered an empty 404).
app.UseRouting();

app.UseMultiTenant();
app.UseSiteRedirects();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();
if (Fuse.Infrastructure.Jobs.HangfireSetup.IsEnabled(app.Configuration))
{
    app.UseJobsDashboard();
    RecurringJobs.Register(app.Services.GetRequiredService<Hangfire.IRecurringJobManager>(), app.Configuration, app.Environment);
}

app.MapStaticAssets();
if (string.IsNullOrWhiteSpace(app.Configuration["AzureStorageSettings:ConnectionString"]))
{
    // Local uploads (no Azure storage configured, e.g. tests): served from {ContentRoot}/.uploads at /uploads.
    var uploads = Fuse.Infrastructure.Files.LocalFileStore.Root(app.Environment);
    Directory.CreateDirectory(uploads);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploads),
        RequestPath = Fuse.Infrastructure.Files.LocalFileStore.RequestPath,
    });
}
app.MapAuthEndpoints();
#if DEBUG
app.MapDevSignIn();
#endif
app.MapContractEndpoints();
app.MapPublicApi();
app.MapStripeWebhook();
app.MapEventsCalendarFeed();
app.MapSiteSeo();
app.MapHostelBooking();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(
        typeof(Fuse.Web.Client.Routes).Assembly,
        typeof(Fuse.Site._Imports).Assembly,
        typeof(Fuse.Admin._Imports).Assembly);

app.Run();

/// <summary>Visible to the integration tests' WebApplicationFactory.</summary>
public partial class Program;
