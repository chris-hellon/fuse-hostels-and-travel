using Finbuckle.MultiTenant.Extensions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Fuse.Application.Common.Persistence;
using Fuse.Application.Common.Security;
using Fuse.Domain.Identity;
using Fuse.Infrastructure.Identity;
using Fuse.Infrastructure.Multitenancy;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Multitenancy;

namespace Fuse.Infrastructure;

public static class DependencyInjection
{
    public const string AuthCookieName = "Travaloud-auth";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("ConnectionStrings:Database is not configured.");
        DatabaseSafety.EnsureLocal(connectionString, configuration.GetValue("Database:AllowNonLocalServer", false));

        services.AddHttpContextAccessor();
        services.AddScoped<CurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());

        AddMultitenancy(services, connectionString);
        AddPersistence(services, connectionString);
        AddIdentity(services);
        AddFileStorage(services, configuration);
        AddCloudbeds(services);
        AddPayments(services, configuration);
        AddMailAndJobs(services, configuration);
        Jobs.HangfireSetup.Add(services, configuration, connectionString);

        return services;
    }

    private static void AddMultitenancy(IServiceCollection services, string connectionString)
    {
        services.AddDbContext<TenantDbContext>(options => options.UseSqlServer(connectionString,
            sql => sql.MigrationsAssembly("Fuse.Migrations")));

        // One tenant, forever (see docs). The static strategy keeps the multi-tenant model intact
        // without resolving anything from the request.
        services.AddMultiTenant<FuseTenantInfo>()
            .WithStaticStrategy(MultitenancyConstants.Fuse.Identifier)
            .WithEFCoreStore<TenantDbContext, FuseTenantInfo>();
    }

    private static void AddFileStorage(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(Files.FileStorageOptions.Section).Get<Files.FileStorageOptions>() ?? new();
        services.Configure<Files.FileStorageOptions>(configuration.GetSection(Files.FileStorageOptions.Section));
        services.AddSingleton<Application.Common.Files.ISpreadsheetWriter, Files.ClosedXmlSpreadsheetWriter>();
        // As legacy: Azure whenever the connection string is set (every environment); tests clear it.
        if (options.UsesAzure)
            services.AddScoped<Application.Common.Files.IFileStore, Files.AzureBlobFileStore>();
        else
            services.AddScoped<Application.Common.Files.IFileStore, Files.LocalFileStore>();
    }

    private static void AddPayments(IServiceCollection services, IConfiguration configuration)
    {
        var stripe = configuration.GetSection(Payments.StripeOptions.Section).Get<Payments.StripeOptions>() ?? new();
        stripe.EnsureSafe();
        services.Configure<Payments.StripeOptions>(configuration.GetSection(Payments.StripeOptions.Section));
        services.AddScoped<Application.Common.Payments.IPaymentGateway, Payments.StripePaymentGateway>();
        services.Configure<Application.Features.Bookings.BookingOptions>(configuration.GetSection(Application.Features.Bookings.BookingOptions.Section));
        services.Configure<Application.Features.Site.SiteOptions>(configuration.GetSection(Application.Features.Site.SiteOptions.Section));
        services.Configure<CaptchaOptions>(configuration.GetSection(CaptchaOptions.Section));
        services.AddHttpClient<ICaptcha, Security.RecaptchaVerifier>(client => client.Timeout = TimeSpan.FromSeconds(10));
    }

    private static void AddMailAndJobs(IServiceCollection services, IConfiguration configuration)
    {

        services.Configure<Mail.MailOptions>(configuration.GetSection(Mail.MailOptions.Section));
        services.Configure<Mail.MailBrand>(configuration.GetSection($"{Mail.MailOptions.Section}:Brand"));
        services.AddSingleton<Mail.GmailAccessTokens>();
        services.AddHttpClient<Application.Common.Mail.IMailbox, Mail.GmailMailbox>(client =>
        {
            client.BaseAddress = new Uri("https://gmail.googleapis.com/gmail/v1/users/me/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<Application.Common.Mail.IEmailSender, Mail.EmailSender>();
        services.AddScoped<Application.Common.Mail.IBookingEmails, Mail.BookingEmails>();
        services.AddScoped<Application.Common.Mail.IEnquiryEmails, Mail.EnquiryEmails>();
        services.AddScoped<Application.Common.Mail.IAccountEmails, Mail.AccountEmails>();

        services.AddSingleton<Application.Common.Payments.IQrCodes, Payments.QrCodes>();
        services.Configure<Payments.BookingWebhookOptions>(configuration.GetSection(Payments.BookingWebhookOptions.Section));
        services.AddHttpClient<Application.Common.Payments.IPaidBookingWebhook, Payments.PaidBookingWebhook>(client => client.Timeout = TimeSpan.FromSeconds(30));
    }

    private static void AddCloudbeds(IServiceCollection services) =>
        services.AddHttpClient<Application.Common.Cloudbeds.ICloudbedsClient, Cloudbeds.CloudbedsClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.cloudbeds.com/api/v1.1/");
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            // No shorter timeout than HttpClient's 100 seconds, as legacy: the guest import's lists can take a while.
        });

    private static void AddPersistence(IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString, sql =>
        {
            sql.MigrationsAssembly("Fuse.Migrations");
            sql.EnableRetryOnFailure(maxRetryCount: 3);
            // A query loading several related lists (e.g. a tour's prices, dates and images) runs as one query per
            // list rather than one query multiplying them all together.
            sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        }));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ITenantContext, TenantContext>();
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies(cookies => cookies.ApplicationCookie?.Configure(options =>
            {
                options.Cookie.Name = AuthCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.SlidingExpiration = true;
                options.LoginPath = "/admin/login";
                options.AccessDeniedPath = "/admin/access-denied";

                // APIs answer 401/403; only page navigations are redirected to sign in.
                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = context => ApiAwareRedirect(context, StatusCodes401),
                    OnRedirectToAccessDenied = context => ApiAwareRedirect(context, StatusCodes403),
                };
            }));

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                // Identity schema v1 matches the existing database (no passkeys table).
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version1;
                options.SignIn.RequireConfirmedAccount = false;
                options.User.RequireUniqueEmail = true;
                // Legacy's forms asked for 6 characters; its Identity options otherwise (a lowercase letter only) are kept.
                options.Password.RequiredLength = Fuse.Contracts.Identity.PasswordRules.MinimumLength;
                options.Password.RequireDigit = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>();
        // Legacy data has shared emails and odd usernames: find and save those accounts as legacy did (see the types).
        services.Replace(ServiceDescriptor.Scoped<IUserStore<ApplicationUser>, LegacyTolerantUserStore>());
        services.Replace(ServiceDescriptor.Scoped<IUserValidator<ApplicationUser>, LegacyTolerantUserValidator>());

        services.AddScoped<Microsoft.AspNetCore.Authentication.IClaimsTransformation, PermissionClaimsTransformation>();
        services.AddScoped<IdentitySeeder>();
    }

    private const int StatusCodes401 = 401;
    private const int StatusCodes403 = 403;

    private static Task ApiAwareRedirect(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }
}
