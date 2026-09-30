using Fuse.Domain.Catalog.Bookings;
using Fuse.Domain.Catalog.Enquiries;
using Fuse.Domain.Catalog.Events;
using Fuse.Domain.Catalog.Properties;
using Fuse.Domain.Catalog.Tours;
using Fuse.Domain.Identity;

namespace Fuse.Infrastructure.Persistence.Configuration;

/// <summary>
/// Column types changed by hand in the real database (lengths tightened during performance work,
/// a few nullability fixes) that the legacy EF model never picked up. Found by comparing the model
/// with the dev database column by column (2026-09-24). Keeping them here makes freshly created
/// databases (tests, new environments) match production; migration AlignModelWithDatabase has an
/// empty Up() because existing databases already have these types.
/// Not aligned on purpose: Properties.TenantId and UserTokens.TenantId are nullable in the database
/// but required by Finbuckle; the stricter model is harmless.
/// </summary>
internal static class HandTunedColumns
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BookingItemRoom>().Property(e => e.CloudbedsGuestId).HasMaxLength(100).IsRequired(false);
        modelBuilder.Entity<BookingItemRoom>().Property(e => e.GuestFirstName).HasMaxLength(300).IsRequired(true);
        modelBuilder.Entity<BookingItemRoom>().Property(e => e.GuestLastName).HasMaxLength(300).IsRequired(true);
        modelBuilder.Entity<BookingItemRoom>().Property(e => e.RoomName).HasMaxLength(100).IsRequired(true);
        modelBuilder.Entity<BookingItem>().Property(e => e.CloudbedsReservationId).HasMaxLength(300).IsRequired(false);
        modelBuilder.Entity<BookingItem>().Property(e => e.PickupLocation).HasMaxLength(500).IsRequired(false);
        modelBuilder.Entity<BookingPayment>().Property(e => e.StripeSessionId).HasMaxLength(450).IsRequired(true);
        modelBuilder.Entity<Booking>().Property(e => e.CurrencyCode).HasMaxLength(10).IsRequired(true);
        modelBuilder.Entity<Event>().Property(e => e.ShortDescription).HasMaxLength(500).IsRequired(false);
        modelBuilder.Entity<GeneralEnquiry>().Property(e => e.ContactNumber).HasMaxLength(100).IsRequired(true);
        modelBuilder.Entity<GeneralEnquiry>().Property(e => e.Email).HasMaxLength(200).IsRequired(true);
        modelBuilder.Entity<GeneralEnquiry>().Property(e => e.Name).HasMaxLength(200).IsRequired(true);
        modelBuilder.Entity<Property>().Property(e => e.AddressLine1).HasMaxLength(100).IsRequired(true);
        modelBuilder.Entity<Property>().Property(e => e.AddressLine2).HasMaxLength(100).IsRequired(false);
        modelBuilder.Entity<Property>().Property(e => e.CloudbedsKey).HasMaxLength(20).IsRequired(false);
        modelBuilder.Entity<Property>().Property(e => e.Name).HasMaxLength(200).IsRequired(true);
        modelBuilder.Entity<Property>().Property(e => e.PageSubTitle).HasMaxLength(300).IsRequired(false);
        modelBuilder.Entity<Property>().Property(e => e.PageTitle).HasMaxLength(200).IsRequired(false);
        modelBuilder.Entity<Property>().Property(e => e.TelephoneNumber).HasMaxLength(30).IsRequired(true);
        modelBuilder.Entity<PropertyDirectionContent>().Property(e => e.Style).HasMaxLength(300).IsRequired(false);
        modelBuilder.Entity<PropertyRoom>().Property(e => e.ImagePath).HasMaxLength(2048).IsRequired(false);
        modelBuilder.Entity<PropertyRoom>().Property(e => e.Name).HasMaxLength(200).IsRequired(true);
        modelBuilder.Entity<PropertyRoom>().Property(e => e.PageSubTitle).HasMaxLength(300).IsRequired(false);
        modelBuilder.Entity<PropertyRoom>().Property(e => e.PageTitle).HasMaxLength(200).IsRequired(false);
        modelBuilder.Entity<PropertyRoom>().Property(e => e.ShortDescription).HasMaxLength(500).IsRequired(false);
        modelBuilder.Entity<TourEnquiry>().Property(e => e.ContactNumber).HasMaxLength(100).IsRequired(true);
        modelBuilder.Entity<TourEnquiry>().Property(e => e.Email).HasMaxLength(200).IsRequired(true);
        modelBuilder.Entity<TourEnquiry>().Property(e => e.Name).HasMaxLength(200).IsRequired(true);
        modelBuilder.Entity<Tour>().Property(e => e.PriceLabel).HasMaxLength(300).IsRequired(false);
        modelBuilder.Entity<ApplicationUser>().Property(e => e.CloudbedsGuestId).HasMaxLength(456).IsRequired(false);
        modelBuilder.Entity<ApplicationUser>().Property(e => e.FullName).HasMaxLength(450).IsRequired(false);
    }
}
