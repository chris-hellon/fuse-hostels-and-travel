using Microsoft.Extensions.DependencyInjection;
using Fuse.Application.Common.Behaviours;

namespace Fuse.Application;

public static class DependencyInjection
{
    /// <summary>Pipeline order: logging wraps authorization wraps validation wraps site-cache clearing wraps the handler.</summary>
    public static readonly Type[] PipelineBehaviours =
    [
        typeof(LoggingBehaviour<,>),
        typeof(AuthorizationBehaviour<,>),
        typeof(ValidationBehaviour<,>),
        typeof(SiteCacheBehaviour<,>),
    ];

    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<Features.Site.SiteCache>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IAccessEvaluator, AccessEvaluator>();
        services.AddScoped<Features.Identity.UserKinds>();
        services.AddScoped<Features.Tours.SupplierScope>();
        services.AddScoped<Features.Bookings.TourCapacity>();
        services.AddScoped<Features.Bookings.BookingRefunds>();
        services.AddScoped<Features.Bookings.SupplierNotices>();
        services.AddScoped<Features.Enquiries.EnquiryInbox>();
        services.AddScoped<Features.Bookings.BookingCheckouts>();
        services.AddScoped<Features.Bookings.PaidBookingNotifications>();
        services.AddScoped<Features.Site.UnpaidCheckouts>();
        services.AddScoped<Features.Manifest.TourManifestEmails>();
        services.AddScoped<Features.Identity.CloudbedsGuestImport>();
        services.AddScoped<Features.Identity.StaffPasswordResetSender>();
        services.AddScoped<Features.Identity.HostelStays>();
        services.AddScoped<Features.Dashboard.GuestsWithoutTours>();
        services.AddScoped<Features.Bookings.HostelBookings>();
        services.AddScoped<Features.Site.SiteEnquiries>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<Common.Time.IBusinessClock, Common.Time.BusinessClock>();

        // Pure validators live next to their contracts (so forms can reuse them); DB-backed ones live here.
        services.AddValidatorsFromAssemblyContaining<ContractMetadata>(ServiceLifetime.Scoped, includeInternalTypes: true);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Scoped, includeInternalTypes: true);

        return services;
    }
}
