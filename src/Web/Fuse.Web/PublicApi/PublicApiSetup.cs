using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Fuse.Shared.Authorization;

namespace Fuse.Web.PublicApi;

/// <summary>
/// The public, machine-to-machine booking API, exactly as legacy served it: /api/token for a JWT from a client's
/// credentials, then /api/users, /api/tours, /api/packages, /api/pack-incentives and /api/bookings (staff tokens only),
/// rate limited, with legacy's Scalar reference at /api/docs over its own OpenAPI document at /api/openapi/v1.json.
/// (The admin's own endpoints live under /api/app and are unrelated.)
/// </summary>
public static class PublicApiSetup
{
    private const string TokenPolicy = "api-token";
    private const string ClientPolicy = "api";

    /// <summary>Marks the public API's endpoints (their rate-limit rejections answer as legacy's did).</summary>
    private sealed class PublicApiEndpoint;

    public static IServiceCollection AddPublicApi(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(ApiSettings.Section).Get<ApiSettings>() ?? new ApiSettings();
        services.Configure<ApiSettings>(configuration.GetSection(ApiSettings.Section));
        services.AddScoped<ApiTokenService>();

        services.AddRateLimiter(options =>
        {
            // Anonymous token endpoint: per caller IP, to blunt credential guessing.
            options.AddPolicy(TokenPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = configuration.GetValue("RateLimits:ApiTokenPerMinute", 10), Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                }));
            // Everything else: per client (its service user), IP as a fallback.
            options.AddPolicy(ClientPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = configuration.GetValue("RateLimits:ApiPerMinute", 120), Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                }));

            var others = options.OnRejected;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<PublicApiEndpoint>() is null)
                {
                    if (others is not null)
                        await others(context, cancellationToken);
                    return;
                }
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
                await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Too many requests. Please slow down and retry later." }, cancellationToken);
            };
        });

        // A bearer scheme beside the admin's cookie (still the default); the API asks for "Bearer" explicitly. Without a
        // key no token can be issued, so validation gets a throwaway one.
        var key = string.IsNullOrWhiteSpace(settings.Jwt.Key) ? Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Guid.NewGuid() : settings.Jwt.Key;
        services.AddAuthentication().AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = settings.Jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = settings.Jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
            });
        return services;
    }

    /// <summary>
    /// Legacy's reference page at /api/docs (any path under it) and its OpenAPI document at /api/openapi/v1.json, never
    /// cached. Early in the pipeline, so nothing downstream (status pages, auth) gets in the way.
    /// </summary>
    public static IApplicationBuilder UsePublicApiDocs(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            var docs = path.StartsWithSegments("/api/docs");
            var spec = path.Equals("/api/openapi/v1.json", StringComparison.OrdinalIgnoreCase);
            if (!HttpMethods.IsGet(context.Request.Method) || (!docs && !spec))
            {
                await next();
                return;
            }

            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
            if (docs)
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(ReferenceHtml);
                return;
            }
            context.Response.ContentType = "application/json; charset=utf-8";
            await using var document = typeof(PublicApiSetup).Assembly.GetManifestResourceStream("Fuse.Web.PublicApi.openapi-v1.json")!;
            await document.CopyToAsync(context.Response.Body);
        });

    /// <summary>Legacy's Scalar reference, pointed at the document with a root-relative URL.</summary>
    private const string ReferenceHtml =
        """
        <!doctype html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>Fuse Booking API</title>
            <link rel="icon" href="data:," />
            <style>body { margin: 0; }</style>
        </head>
        <body>
            <script id="api-reference" data-url="/api/openapi/v1.json"></script>
            <script>
                var configuration = { theme: 'default', hideDownloadButton: false };
                document.getElementById('api-reference').dataset.configuration = JSON.stringify(configuration);
            </script>
            <script src="https://cdn.jsdelivr.net/npm/@scalar/api-reference"></script>
        </body>
        </html>
        """;

    public static IEndpointRouteBuilder MapPublicApi(this IEndpointRouteBuilder app)
    {
        // Public: the credential exchange.
        var open = app.MapGroup("/api").DisableAntiforgery().RequireRateLimiting(TokenPolicy).WithMetadata(new PublicApiEndpoint())
            .ExcludeFromDescription();
        open.MapTokenEndpoint();

        // Everything else: a valid bearer token for a staff user.
        var secured = app.MapGroup("/api").DisableAntiforgery().RequireRateLimiting(ClientPolicy).WithMetadata(new PublicApiEndpoint())
            .ExcludeFromDescription()
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireRole(FuseRoles.StaffRoles));
        secured.MapPublicUsers();
        secured.MapPublicTours();
        secured.MapPublicPackages();
        secured.MapPublicPackIncentives();
        secured.MapPublicBookings();
        return app;
    }
}
