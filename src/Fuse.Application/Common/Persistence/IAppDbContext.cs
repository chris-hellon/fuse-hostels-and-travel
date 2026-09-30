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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Fuse.Application.Common.Persistence;

/// <summary>
/// The unit of work handlers query directly. Tenant isolation and soft-delete are applied as
/// global query filters, so handlers never filter on TenantId or DeletedOn themselves.
/// </summary>
public interface IAppDbContext
{
    DbSet<Destination> Destinations { get; }
    DbSet<Tour> Tours { get; }
    DbSet<TourCategory> TourCategories { get; }
    DbSet<TourDate> TourDates { get; }
    DbSet<TourItinerary> TourItineraries { get; }
    DbSet<TourCategoryLookup> TourCategoryLookups { get; }
    DbSet<TourDestinationLookup> TourDestinationLookups { get; }
    DbSet<Property> Properties { get; }
    DbSet<PropertyDestinationLookup> PropertyDestinationLookups { get; }
    DbSet<PropertyDirection> PropertyDirections { get; }
    DbSet<PropertyDirectionContent> PropertyDirectionContents { get; }
    DbSet<PropertyFacility> PropertyFacilities { get; }
    DbSet<PropertyRoom> PropertyRooms { get; }
    DbSet<PropertyImage> PropertyImages { get; }
    DbSet<Event> Events { get; }
    DbSet<EventCategory> EventCategories { get; }
    DbSet<EventPropertyLookup> EventPropertyLookups { get; }
    DbSet<EventOccurrenceException> EventOccurrenceExceptions { get; }
    DbSet<TourManifestEmail> TourManifestEmails { get; }
    DbSet<EventTimeSlot> EventTimeSlots { get; }
    DbSet<TourPropertyLookup> TourPropertyLookup { get; }
    DbSet<Booking> Bookings { get; }
    DbSet<BookingItem> BookingItems { get; }
    DbSet<BookingPayment> BookingPayments { get; }
    DbSet<BookingItemRoom> BookingItemRooms { get; }
    DbSet<BookingItemGuest> BookingItemGuests { get; }
    DbSet<BookingPackage> BookingPackages { get; }
    DbSet<Package> Packages { get; }
    DbSet<PackageTier> PackageTiers { get; }
    DbSet<PackageItem> PackageItems { get; }
    DbSet<PackageItemPrice> PackageItemPrices { get; }
    DbSet<PackIncentive> PackIncentives { get; }
    DbSet<PackIncentiveTour> PackIncentiveTours { get; }
    DbSet<PackIncentiveTier> PackIncentiveTiers { get; }
    DbSet<TourPrice> TourPrices { get; }
    DbSet<TourImage> TourImages { get; }
    DbSet<TourItinerarySection> TourItinerarySections { get; }
    DbSet<TourItinerarySectionImage> TourItinerarySectionImages { get; }
    DbSet<DestinationTourCategoryLookup> DestinationTourCategoryLookups { get; }
    DbSet<Service> Services { get; }
    DbSet<ServiceField> ServiceFields { get; }
    DbSet<JobVacancy> JobVacancies { get; }
    DbSet<JobVacancyResponse> JobVacancyResponses { get; }
    DbSet<Page> Pages { get; }
    DbSet<PageSorting> PageSortings { get; }
    DbSet<PageModal> PageModals { get; }
    DbSet<PageModalLookup> PageModalLookups { get; }
    DbSet<Partner> Partners { get; }
    DbSet<PartnerContact> PartnerContacts { get; }
    DbSet<TourEnquiry> TourEnquiries { get; }
    DbSet<ServiceEnquiry> ServiceEnquiries { get; }
    DbSet<ServiceEnquiryField> ServiceEnquiryFields { get; }
    DbSet<GeneralEnquiry> GeneralEnquiries { get; }
    DbSet<Gallery> Galleries { get; }
    DbSet<GalleryImage> GalleryImages { get; }
    DbSet<TravelGuide> TravelGuides { get; }
    DbSet<TravelGuideGalleryImage> TravelGuideGalleryImages { get; }
    DbSet<TourPickupLocation> TourPickupLocations { get; }
    DbSet<Seo> Seo { get; }
    DbSet<SeoRedirect> SeoRedirects { get; }

    DbSet<ApplicationUser> Users { get; }
    DbSet<ApplicationRole> Roles { get; }
    DbSet<IdentityUserRole<string>> UserRoles { get; }
    DbSet<ApplicationRoleClaim> RoleClaims { get; }
    DbSet<Trail> AuditTrails { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
