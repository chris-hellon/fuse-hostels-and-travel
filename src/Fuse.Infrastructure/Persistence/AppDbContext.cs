using System.Data;
using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata;
using Fuse.Application.Common.Persistence;
using Fuse.Application.Common.Security;
using Fuse.Domain.Auditing;
using Fuse.Domain.Catalog.Bookings;
using Fuse.Domain.Catalog.Commission;
using Fuse.Domain.Catalog.Destinations;
using Fuse.Domain.Catalog.Enquiries;
using Fuse.Domain.Catalog.Events;
using Fuse.Domain.Catalog.Galleries;
using Fuse.Domain.Catalog.JobVacancies;
using Fuse.Domain.Catalog.Packages;
using Fuse.Domain.Catalog.Pages;
using Fuse.Domain.Catalog.Partners;
using Fuse.Domain.Catalog.Properties;
using Fuse.Domain.Catalog.SEO;
using Fuse.Domain.Catalog.Services;
using Fuse.Domain.Catalog.Tours;
using Fuse.Domain.Catalog.TravelGuides;
using Fuse.Domain.Identity;
using Fuse.Infrastructure.Persistence.Configuration;

namespace Fuse.Infrastructure.Persistence;

public sealed class AppDbContext(
    IMultiTenantContextAccessor tenantAccessor,
    DbContextOptions<AppDbContext> options,
    ICurrentUser currentUser,
    IPublisher publisher)
    : MultiTenantIdentityDbContext<ApplicationUser, ApplicationRole, string, IdentityUserClaim<string>,
        IdentityUserRole<string>, IdentityUserLogin<string>, ApplicationRoleClaim, IdentityUserToken<string>,
        IdentityUserPasskey<string>>(tenantAccessor, options), IAppDbContext
{
    public const string SoftDeleteFilter = "SoftDelete";

    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<Tour> Tours => Set<Tour>();
    public DbSet<TourCategory> TourCategories => Set<TourCategory>();
    public DbSet<TourDate> TourDates => Set<TourDate>();
    public DbSet<TourItinerary> TourItineraries => Set<TourItinerary>();
    public DbSet<TourCategoryLookup> TourCategoryLookups => Set<TourCategoryLookup>();
    public DbSet<TourDestinationLookup> TourDestinationLookups => Set<TourDestinationLookup>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<PropertyDestinationLookup> PropertyDestinationLookups => Set<PropertyDestinationLookup>();
    public DbSet<PropertyDirection> PropertyDirections => Set<PropertyDirection>();
    public DbSet<PropertyDirectionContent> PropertyDirectionContents => Set<PropertyDirectionContent>();
    public DbSet<PropertyFacility> PropertyFacilities => Set<PropertyFacility>();
    public DbSet<PropertyRoom> PropertyRooms => Set<PropertyRoom>();
    public DbSet<PropertyImage> PropertyImages => Set<PropertyImage>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventCategory> EventCategories => Set<EventCategory>();
    public DbSet<EventPropertyLookup> EventPropertyLookups => Set<EventPropertyLookup>();
    public DbSet<EventOccurrenceException> EventOccurrenceExceptions => Set<EventOccurrenceException>();
    public DbSet<EventTimeSlot> EventTimeSlots => Set<EventTimeSlot>();
    public DbSet<TourManifestEmail> TourManifestEmails => Set<TourManifestEmail>();
    public DbSet<TourPropertyLookup> TourPropertyLookup => Set<TourPropertyLookup>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingItem> BookingItems => Set<BookingItem>();
    public DbSet<BookingPayment> BookingPayments => Set<BookingPayment>();
    public DbSet<BookingItemRoom> BookingItemRooms => Set<BookingItemRoom>();
    public DbSet<BookingItemGuest> BookingItemGuests => Set<BookingItemGuest>();
    public DbSet<BookingPackage> BookingPackages => Set<BookingPackage>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<PackageTier> PackageTiers => Set<PackageTier>();
    public DbSet<PackageItem> PackageItems => Set<PackageItem>();
    public DbSet<PackageItemPrice> PackageItemPrices => Set<PackageItemPrice>();
    public DbSet<PackIncentive> PackIncentives => Set<PackIncentive>();
    public DbSet<PackIncentiveTour> PackIncentiveTours => Set<PackIncentiveTour>();
    public DbSet<PackIncentiveTier> PackIncentiveTiers => Set<PackIncentiveTier>();
    public DbSet<TourPrice> TourPrices => Set<TourPrice>();
    public DbSet<TourImage> TourImages => Set<TourImage>();
    public DbSet<TourItinerarySection> TourItinerarySections => Set<TourItinerarySection>();
    public DbSet<TourItinerarySectionImage> TourItinerarySectionImages => Set<TourItinerarySectionImage>();
    public DbSet<DestinationTourCategoryLookup> DestinationTourCategoryLookups => Set<DestinationTourCategoryLookup>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<ServiceField> ServiceFields => Set<ServiceField>();
    public DbSet<JobVacancy> JobVacancies => Set<JobVacancy>();
    public DbSet<JobVacancyResponse> JobVacancyResponses => Set<JobVacancyResponse>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<PageSorting> PageSortings => Set<PageSorting>();
    public DbSet<PageModal> PageModals => Set<PageModal>();
    public DbSet<PageModalLookup> PageModalLookups => Set<PageModalLookup>();
    public DbSet<Partner> Partners => Set<Partner>();
    public DbSet<PartnerContact> PartnerContacts => Set<PartnerContact>();
    public DbSet<TourEnquiry> TourEnquiries => Set<TourEnquiry>();
    public DbSet<ServiceEnquiry> ServiceEnquiries => Set<ServiceEnquiry>();
    public DbSet<ServiceEnquiryField> ServiceEnquiryFields => Set<ServiceEnquiryField>();
    public DbSet<GeneralEnquiry> GeneralEnquiries => Set<GeneralEnquiry>();
    public DbSet<Gallery> Galleries => Set<Gallery>();
    public DbSet<GalleryImage> GalleryImages => Set<GalleryImage>();
    public DbSet<TravelGuide> TravelGuides => Set<TravelGuide>();
    public DbSet<TravelGuideGalleryImage> TravelGuideGalleryImages => Set<TravelGuideGalleryImage>();
    public DbSet<TourPickupLocation> TourPickupLocations => Set<TourPickupLocation>();
    public DbSet<Seo> Seo => Set<Seo>();
    public DbSet<SeoRedirect> SeoRedirects => Set<SeoRedirect>();

    public DbSet<Trail> AuditTrails => Set<Trail>();

    /// <summary>For Dapper queries and stored procedures that share this unit of work.</summary>
    public IDbConnection Connection => Database.GetDbConnection();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every money/duration column in the existing schema is decimal(18,2).
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        // Read entities without running legacy constructor logic (see the convention).
        configurationBuilder.Conventions.Add(_ => new ParameterlessConstructorConvention());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaNames.Catalog);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly,
            t => t.Namespace == typeof(SchemaNames).Namespace);

        // Soft-deleted rows disappear everywhere; a named filter so it composes with the tenant filter.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(e => e.BaseType is null && typeof(ISoftDelete).IsAssignableFrom(e.ClrType)))
        {
            var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
            var deletedOn = System.Linq.Expressions.Expression.Property(parameter, nameof(ISoftDelete.DeletedOn));
            var isNotDeleted = System.Linq.Expressions.Expression.Equal(deletedOn,
                System.Linq.Expressions.Expression.Constant(null, typeof(DateTime?)));
            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(SoftDeleteFilter, System.Linq.Expressions.Expression.Lambda(isNotDeleted, parameter));
        }

        KeepLegacyTenancyConventions(modelBuilder);
        HandTunedColumns.Apply(modelBuilder);

        modelBuilder.Entity<Booking>().Property(b => b.InvoiceId)
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }

    /// <summary>
    /// The database was created by Finbuckle 6, whose conventions differ from v10. Reproduce them
    /// so the model matches the existing schema (verified by scaffolding an empty migration).
    /// </summary>
    private static void KeepLegacyTenancyConventions(ModelBuilder modelBuilder)
    {
        foreach (var tenantId in modelBuilder.Model.GetEntityTypes()
                     .Select(e => e.FindProperty("TenantId"))
                     .OfType<IMutableProperty>())
        {
            tenantId.SetMaxLength(64);
        }

        // v6 keyed UserLogins on a surrogate Id, with the natural key unique per tenant.
        modelBuilder.Entity<IdentityUserLogin<string>>(login =>
        {
            login.Property<string>("Id").ValueGeneratedOnAdd();
            login.HasKey("Id");
            login.Property(l => l.LoginProvider).IsRequired();
            login.Property(l => l.ProviderKey).IsRequired();
            login.HasIndex(nameof(IdentityUserLogin<string>.LoginProvider), nameof(IdentityUserLogin<string>.ProviderKey), "TenantId")
                .IsUnique();
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditing(currentUser.UserId ?? DefaultIdType.Empty);

        var result = await base.SaveChangesAsync(cancellationToken);

        await PublishDomainEventsAsync(cancellationToken);

        return result;
    }

    private void ApplyAuditing(DefaultIdType userId)
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added when entry.Entity.OverrideCreatedBy:
                    entry.Entity.LastModifiedBy = entry.Entity.CreatedBy;
                    break;

                case EntityState.Added:
                    entry.Entity.CreatedBy = userId;
                    entry.Entity.LastModifiedBy = userId;
                    break;

                case EntityState.Modified:
                    entry.Entity.LastModifiedOn = now;
                    entry.Entity.LastModifiedBy = userId;
                    break;

                case EntityState.Deleted when entry.Entity is ISoftDelete softDelete:
                    softDelete.DeletedBy = userId;
                    softDelete.DeletedOn = now;
                    entry.State = EntityState.Modified;
                    break;
            }
        }
    }

    private async Task PublishDomainEventsAsync(CancellationToken cancellationToken)
    {
        var events = ChangeTracker.Entries<IEntity>()
            .SelectMany(e =>
            {
                var raised = e.Entity.DomainEvents.ToArray();
                e.Entity.DomainEvents.Clear();
                return raised;
            })
            .ToArray();

        foreach (var domainEvent in events)
            await publisher.Publish(domainEvent, cancellationToken);
    }
}
