using Microsoft.EntityFrameworkCore.Metadata.Builders;
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

namespace Fuse.Infrastructure.Persistence.Configuration;

internal sealed class DestinationConfig : IEntityTypeConfiguration<Destination>
{
    public void Configure(EntityTypeBuilder<Destination> builder)
    {
        builder.IsMultiTenant();

        builder.Property(b => b.Name).HasColumnType("nvarchar(1024)");
        builder.Property(x => x.Description).HasColumnType("nvarchar(max)");
        builder.Property(x => x.ShortDescription).HasColumnType("nvarchar(3000)");
        builder.Property(x => x.Directions).HasColumnType("nvarchar(max)");
        builder.Property(x => x.GoogleMapsKey).HasColumnType("nvarchar(300)");

        builder.Property(p => p.ImagePath).HasMaxLength(2048);
        builder.Property(p => p.ThumbnailImagePath).HasMaxLength(2048);
    }
}

internal sealed class PropertiesConfig : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> builder)
    {
        builder.IsMultiTenant();

        builder.Property(p => p.ImagePath).HasMaxLength(2048);
        builder.Property(p => p.ThumbnailImagePath).HasMaxLength(2048);
        builder.Property(p => p.VideoPath).HasMaxLength(2048);
        builder.Property(p => p.MobileVideoPath).HasMaxLength(2048);
    }
}

internal sealed class BookingsConfig : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.IsMultiTenant();

        // Optional "pack" incentive mark; deleting a pack must not cascade to bookings.
        builder.HasOne<PackIncentive>()
            .WithMany()
            .HasForeignKey(b => b.PackIncentiveId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PackIncentiveConfig : IEntityTypeConfiguration<PackIncentive>
{
    public void Configure(EntityTypeBuilder<PackIncentive> builder)
    {
        builder.IsMultiTenant();

        builder.Property(p => p.Name).HasColumnType("nvarchar(200)").HasMaxLength(200);
        builder.Property(p => p.Notes).HasColumnType("nvarchar(max)");

        builder.HasMany(p => p.Tours)
            .WithOne()
            .HasForeignKey(t => t.PackIncentiveId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Tiers)
            .WithOne()
            .HasForeignKey(t => t.PackIncentiveId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PackIncentiveTierConfig : IEntityTypeConfiguration<PackIncentiveTier>
{
    public void Configure(EntityTypeBuilder<PackIncentiveTier> builder)
    {
        builder.IsMultiTenant();

        builder.Property(t => t.Title).HasColumnType("nvarchar(200)").HasMaxLength(200);
    }
}

internal sealed class PackIncentiveTourConfig : IEntityTypeConfiguration<PackIncentiveTour>
{
    public void Configure(EntityTypeBuilder<PackIncentiveTour> builder)
    {
        builder.IsMultiTenant();

        // The tour link is a lookup; removing a tour shouldn't cascade-delete pack rows.
        builder.HasOne(t => t.Tour)
            .WithMany()
            .HasForeignKey(t => t.TourId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class BookingPackagesConfig : IEntityTypeConfiguration<BookingPackage>
{
    public void Configure(EntityTypeBuilder<BookingPackage> builder)
    {
        builder.IsMultiTenant();

        builder.Property(p => p.PackageName).HasColumnType("nvarchar(1024)").HasMaxLength(1024);

        builder.HasMany(p => p.Items)
            .WithOne(i => i.BookingPackage)
            .HasForeignKey(i => i.BookingPackageId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PackageConfig : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> builder)
    {
        builder.IsMultiTenant();

        builder.Property(b => b.Name).HasColumnType("nvarchar(1024)").HasMaxLength(1024);
        builder.Property(x => x.Description).HasColumnType("nvarchar(max)");
        builder.Property(x => x.ShortDescription).HasColumnType("nvarchar(3000)").HasMaxLength(3000);
        builder.Property(x => x.PriceLabel).HasColumnType("nvarchar(200)").HasMaxLength(200);
        builder.Property(x => x.Notes).HasColumnType("nvarchar(max)");

        builder.Property(p => p.ImagePath).HasMaxLength(2048);
        builder.Property(p => p.ThumbnailImagePath).HasMaxLength(2048);

        builder.HasOne(p => p.TourCategory)
            .WithMany()
            .HasForeignKey(p => p.TourCategoryId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(p => p.PackageTiers)
            .WithOne()
            .HasForeignKey(t => t.PackageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PackageTierConfig : IEntityTypeConfiguration<PackageTier>
{
    public void Configure(EntityTypeBuilder<PackageTier> builder)
    {
        builder.IsMultiTenant();

        builder.Property(b => b.Title).HasColumnType("nvarchar(200)").HasMaxLength(200);
    }
}

internal sealed class PackageItemConfig : IEntityTypeConfiguration<PackageItem>
{
    public void Configure(EntityTypeBuilder<PackageItem> builder)
    {
        builder.IsMultiTenant();

        builder.HasOne(i => i.Tour)
            .WithMany()
            .HasForeignKey(i => i.TourId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(i => i.PackageItemPrices)
            .WithOne()
            .HasForeignKey(p => p.PackageItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PackageItemPriceConfig : IEntityTypeConfiguration<PackageItemPrice>
{
    public void Configure(EntityTypeBuilder<PackageItemPrice> builder)
    {
        builder.IsMultiTenant();

        // Avoid a second cascade path (prices already cascade from PackageItem).
        builder.HasOne(p => p.PackageTier)
            .WithMany()
            .HasForeignKey(p => p.PackageTierId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}


internal sealed class TourConfig : IEntityTypeConfiguration<Tour>
{
    public void Configure(EntityTypeBuilder<Tour> builder)
    {
        builder.IsMultiTenant();

        builder.Property(b => b.Name).HasColumnType("nvarchar(1024)").HasMaxLength(1024);
        builder.Property(x => x.Description).HasColumnType("nvarchar(max)");
        builder.Property(x => x.ShortDescription).HasColumnType("nvarchar(3000)").HasMaxLength(3000);
        builder.Property(x => x.Address).HasColumnType("nvarchar(300)").HasMaxLength(300);
        builder.Property(x => x.TelephoneNumber).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(x => x.WhatsIncluded).HasColumnType("nvarchar(max)");
        builder.Property(x => x.WhatsNotIncluded).HasColumnType("nvarchar(max)");
        builder.Property(x => x.AdditionalInformation).HasColumnType("nvarchar(max)");

        builder.Property(p => p.ImagePath).HasMaxLength(2048);
        builder.Property(p => p.ThumbnailImagePath).HasMaxLength(2048);
        builder.Property(p => p.VideoPath).HasMaxLength(2048);
        builder.Property(p => p.MobileVideoPath).HasMaxLength(2048);
    }
}

internal sealed class TourCategoryConfig : IEntityTypeConfiguration<TourCategory>
{
    public void Configure(EntityTypeBuilder<TourCategory> builder)
    {
        builder.IsMultiTenant();

        builder.Property(b => b.Name).HasColumnType("nvarchar(1024)").HasMaxLength(1024);
        builder.Property(x => x.Description).HasColumnType("nvarchar(max)");
        builder.Property(x => x.ShortDescription).HasColumnType("nvarchar(3000)").HasMaxLength(3000);

        builder.Property(p => p.ImagePath).HasMaxLength(2048);
        builder.Property(p => p.ThumbnailImagePath).HasMaxLength(2048);
    }
}

internal sealed class TourDateConfig : IEntityTypeConfiguration<TourDate>
{
    public void Configure(EntityTypeBuilder<TourDate> builder)
    {
        builder.IsMultiTenant();
    }
}

internal sealed class TourItineraryConfig : IEntityTypeConfiguration<TourItinerary>
{
    public void Configure(EntityTypeBuilder<TourItinerary> builder)
    {
        builder.Property(b => b.Header).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(x => x.Title).HasColumnType("nvarchar(200)").HasMaxLength(200);
        builder.Property(x => x.Description).HasColumnType("nvarchar(max)");

        builder.IsMultiTenant();
    }
}

internal sealed class TourItinerarySectionConfig : IEntityTypeConfiguration<TourItinerarySection>
{
    public void Configure(EntityTypeBuilder<TourItinerarySection> builder)
    {
        builder.IsMultiTenant();

        builder.Property(p => p.Title).HasMaxLength(300);
        builder.Property(p => p.SubTitle).HasMaxLength(300);
    }
}

internal sealed class TourItinerarySectionImageConfig : IEntityTypeConfiguration<TourItinerarySectionImage>
{
    public void Configure(EntityTypeBuilder<TourItinerarySectionImage> builder)
    {
        builder.IsMultiTenant();

        builder.Property(p => p.ImagePath).HasMaxLength(2048);
        builder.Property(p => p.ThumbnailImagePath).HasMaxLength(2048);
    }
}

internal sealed class TourCategoryLookupConfig : IEntityTypeConfiguration<TourCategoryLookup>
{
    public void Configure(EntityTypeBuilder<TourCategoryLookup> builder)
    {
        builder.IsMultiTenant();
    }
}

internal sealed class TourDestinationLookupConfig : IEntityTypeConfiguration<TourDestinationLookup>
{
    public void Configure(EntityTypeBuilder<TourDestinationLookup> builder)
    {
        builder.IsMultiTenant();
    }
}

internal sealed class DestinationTourCategoryLookupConfig : IEntityTypeConfiguration<DestinationTourCategoryLookup>
{
    public void Configure(EntityTypeBuilder<DestinationTourCategoryLookup> builder)
    {
        builder.IsMultiTenant();
    }
}

internal sealed class SercicesConfig : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.IsMultiTenant();
    }
}

internal sealed class SerciceFieldsConfig : IEntityTypeConfiguration<ServiceField>
{
    public void Configure(EntityTypeBuilder<ServiceField> builder)
    {
        builder.IsMultiTenant();
    }
}

internal sealed class JobVacanciesLookupConfig : IEntityTypeConfiguration<JobVacancy>
{
    public void Configure(EntityTypeBuilder<JobVacancy> builder)
    {
        builder.IsMultiTenant();
    }
}

internal sealed class JobVacancyResponsesConfig : IEntityTypeConfiguration<JobVacancyResponse>
{
    public void Configure(EntityTypeBuilder<JobVacancyResponse> builder)
    {
        builder.Property(p => p.Email).HasMaxLength(256);
        builder.Property(p => p.FirstName).HasMaxLength(256);
        builder.Property(p => p.LastName).HasMaxLength(256);
        builder.Property(p => p.HowCanWeCollaborate).HasMaxLength(2000);
        builder.Property(p => p.EstimatedDates).HasMaxLength(500);
        builder.Property(p => p.DestinationsVisited).HasMaxLength(500);
        builder.Property(p => p.Instagram).HasMaxLength(256);
        builder.Property(p => p.TikTok).HasMaxLength(256);
        builder.Property(p => p.YouTube).HasMaxLength(256);
        builder.Property(p => p.Portfolio).HasMaxLength(256);
        builder.Property(p => p.Equipment).HasMaxLength(500);
    }
}

internal sealed class PageLookupConfig : IEntityTypeConfiguration<Page>
{
    public void Configure(EntityTypeBuilder<Page> builder)
    {
        builder.IsMultiTenant();
    }
}

internal sealed class PageSortingLookupConfig : IEntityTypeConfiguration<PageSorting>
{
    public void Configure(EntityTypeBuilder<PageSorting> builder)
    {
        builder.IsMultiTenant();
    }
}

internal sealed class PageModalsConfig : IEntityTypeConfiguration<PageModal>
{
    public void Configure(EntityTypeBuilder<PageModal> builder)
    {
        builder.IsMultiTenant();
    }
}

internal sealed class PageModalLookupsConfig : IEntityTypeConfiguration<PageModalLookup>
{
    public void Configure(EntityTypeBuilder<PageModalLookup> builder)
    {

    }
}


internal sealed class PartnersConfig : IEntityTypeConfiguration<Partner>
{
    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        builder.IsMultiTenant();
    }
}

internal sealed class PartnerContactsConfig : IEntityTypeConfiguration<PartnerContact>
{
    public void Configure(EntityTypeBuilder<PartnerContact> builder)
    {
        builder.IsMultiTenant();
    }
}

internal sealed class EventsConfig : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.IsMultiTenant();

        builder.HasOne(e => e.EventCategory)
            .WithMany(c => c.Events)
            .HasForeignKey(e => e.EventCategoryId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(e => e.EventPropertyLookups)
            .WithOne(l => l.Event)
            .HasForeignKey(l => l.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.OccurrenceExceptions)
            .WithOne(x => x.Event)
            .HasForeignKey(x => x.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.TimeSlots)
            .WithOne(x => x.Event)
            .HasForeignKey(x => x.EventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EventCategoriesConfig : IEntityTypeConfiguration<EventCategory>
{
    public void Configure(EntityTypeBuilder<EventCategory> builder) => builder.IsMultiTenant();
}

internal sealed class EventPropertyLookupConfig : IEntityTypeConfiguration<EventPropertyLookup>
{
    public void Configure(EntityTypeBuilder<EventPropertyLookup> builder)
    {
        builder.IsMultiTenant();

        // Don't cascade a Property delete through the lookup (avoids multiple cascade paths).
        builder.HasOne(l => l.Property)
            .WithMany()
            .HasForeignKey(l => l.PropertyId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class EventOccurrenceExceptionConfig : IEntityTypeConfiguration<EventOccurrenceException>
{
    public void Configure(EntityTypeBuilder<EventOccurrenceException> builder) => builder.IsMultiTenant();
}

internal sealed class TourManifestEmailConfig : IEntityTypeConfiguration<TourManifestEmail>
{
    public void Configure(EntityTypeBuilder<TourManifestEmail> builder)
    {
        builder.IsMultiTenant();
        // Claiming a departure is inserting its row: a second server (or tick) trying the same one fails here.
        builder.HasIndex(m => new { m.TourId, m.StartDate }).IsUnique();
    }
}

internal sealed class EventTimeSlotConfig : IEntityTypeConfiguration<EventTimeSlot>
{
    public void Configure(EntityTypeBuilder<EventTimeSlot> builder) => builder.IsMultiTenant();
}

internal sealed class TourEnquiriesConfig : IEntityTypeConfiguration<TourEnquiry>
{
    public void Configure(EntityTypeBuilder<TourEnquiry> builder)
    {
        builder.IsMultiTenant();
    }
}

internal sealed class ServiceEnquiriesConfig : IEntityTypeConfiguration<ServiceEnquiry>
{
    public void Configure(EntityTypeBuilder<ServiceEnquiry> builder)
    {
        builder.IsMultiTenant();
    }
}

internal sealed class GeneralEnquiriesConfig : IEntityTypeConfiguration<GeneralEnquiry>
{
    public void Configure(EntityTypeBuilder<GeneralEnquiry> builder)
    {
        builder.IsMultiTenant();
    }
}

internal sealed class GalleriesConfig : IEntityTypeConfiguration<Gallery>
{
    public void Configure(EntityTypeBuilder<Gallery> builder)
    {
        builder.IsMultiTenant();
    }
}

internal sealed class TravelGuidesConfig : IEntityTypeConfiguration<TravelGuide>
{
    public void Configure(EntityTypeBuilder<TravelGuide> builder)
    {
        builder.IsMultiTenant();
    }
}

internal sealed class SeoConfig : IEntityTypeConfiguration<Seo>
{
    public void Configure(EntityTypeBuilder<Seo> builder)
    {
        builder.IsMultiTenant();
    }
}

internal sealed class SeoRedirectConfig : IEntityTypeConfiguration<SeoRedirect>
{
    public void Configure(EntityTypeBuilder<SeoRedirect> builder)
    {
        builder.IsMultiTenant();
    }
}