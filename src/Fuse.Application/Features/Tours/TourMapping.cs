using Fuse.Contracts.Tours;
using Fuse.Domain.Catalog.Tours;

namespace Fuse.Application.Features.Tours;

internal static class TourMapping
{
    public static TourDetails ToDomain(this TourInput input) => new()
    {
        Name = input.Name,
        Description = input.Description,
        ShortDescription = input.ShortDescription,
        WhatsIncluded = input.WhatsIncluded,
        WhatsNotIncluded = input.WhatsNotIncluded,
        AdditionalInformation = input.AdditionalInformation,
        ImportantInformation = input.ImportantInformation,
        TelephoneNumber = input.TelephoneNumber,
        DayDuration = input.DayDuration,
        NightDuration = input.NightDuration,
        HourDuration = input.HourDuration,
        MinCapacity = input.MinCapacity,
        MaxCapacity = input.MaxCapacity,
        PublishToSite = input.PublishToSite,
        TourCategoryId = input.TourCategoryId,
        SupplierId = input.SupplierId,
        WaiverRequired = input.WaiverRequired,
        ShowBookingQRCode = input.ShowBookingQRCode,
        ExcludeFromStaffSales = input.ExcludeFromStaffSales,
        AdditionalGuestDetailsRequired = input.AdditionalGuestDetailsRequired,
        BookingConfirmationEmailDetails = input.BookingConfirmationEmailDetails,
        TermsAndConditions = input.TermsAndConditions,
        CancellationPolicy = input.CancellationPolicy,
        SeoPageTitle = input.SeoPageTitle,
        H1 = input.H1,
        H2 = input.H2,
        UrlSlug = input.UrlSlug,
        MetaKeywords = input.MetaKeywords,
        MetaDescription = input.MetaDescription,
        CustomSeoScripts = input.CustomSeoScripts,
    };

    public static TourPriceDetails ToDomain(this TourPriceInput input) => new()
    {
        Price = input.Price,
        Title = input.Title,
        Description = input.Description,
        MonthFrom = input.MonthFrom,
        MonthTo = input.MonthTo,
        DayDuration = input.DayDuration,
        NightDuration = input.NightDuration,
        HourDuration = input.HourDuration,
        CommissionAmount = input.CommissionAmount,
        CommissionMinimumPax = input.CommissionMinimumPax,
        CommissionMinimumPaxScope = input.CommissionScope is { } scope ? (CommissionMinimumPaxScope)(int)scope : null,
        PublishToWebsite = input.PublishToWebsite,
        InHouseOnly = input.InHouseOnly,
    };

    public static TourPriceInput ToInput(this TourPrice price) => new()
    {
        Price = price.Price,
        Title = price.Title,
        Description = price.Description,
        MonthFrom = price.MonthFrom,
        MonthTo = price.MonthTo,
        DayDuration = price.DayDuration,
        NightDuration = price.NightDuration,
        HourDuration = price.HourDuration,
        CommissionAmount = price.ComissionAmount,
        CommissionMinimumPax = price.CommissionMinimumPax,
        CommissionScope = price.CommissionMinimumPaxScope is { } scope ? (CommissionScope)(int)scope : null,
        PublishToWebsite = price.PublishToWebsite == true,
        InHouseOnly = price.InHouseOnly == true,
    };

    /// <summary>The CMS page that carries a tour's SEO on the public site ("Tours - {Name}", as before).</summary>
    public static string PageTitle(string tourName) => $"Tours - {tourName}";
}

/// <summary>Shared lookups: a tour must exist, and a departure is "booked" if any booking item points at it.</summary>
internal static class TourQueries
{
    public static async Task<Tour> FindTourAsync(this IAppDbContext db, DefaultIdType id, CancellationToken cancellationToken) =>
        await db.Tours.FirstOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw new NotFoundException("Tour not found.");

    public static async Task EnsureTourExistsAsync(this IAppDbContext db, DefaultIdType id, CancellationToken cancellationToken)
    {
        if (!await db.Tours.AnyAsync(t => t.Id == id, cancellationToken))
            throw new NotFoundException("Tour not found.");
    }

    public static IQueryable<TourDate> Upcoming(this IQueryable<TourDate> dates) => dates.Where(d => d.StartDate > DateTime.UtcNow);

    /// <summary>Departure ids (of the given ones) that have at least one booking.</summary>
    public static async Task<HashSet<DefaultIdType>> BookedAsync(this IAppDbContext db, IReadOnlyCollection<DefaultIdType> dateIds, CancellationToken cancellationToken) =>
        [.. await db.BookingItems.Where(i => i.TourDateId != null && dateIds.Contains(i.TourDateId.Value))
            .Select(i => i.TourDateId!.Value).Distinct().ToListAsync(cancellationToken)];
}
