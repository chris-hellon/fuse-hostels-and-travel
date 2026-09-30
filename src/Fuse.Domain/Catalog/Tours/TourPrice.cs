using Fuse.Shared.Tours;

namespace Fuse.Domain.Catalog.Tours;

public sealed record TourPriceDetails
{
    public required decimal Price { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? MonthFrom { get; init; }
    public string? MonthTo { get; init; }
    public decimal? DayDuration { get; init; }
    public decimal? NightDuration { get; init; }
    public decimal? HourDuration { get; init; }
    public decimal? CommissionAmount { get; init; }
    public int? CommissionMinimumPax { get; init; }
    public CommissionMinimumPaxScope? CommissionMinimumPaxScope { get; init; }
    public bool PublishToWebsite { get; init; }
    public bool InHouseOnly { get; init; }
}

/// <summary>A price option for a tour ("Per Person", "Private"...). Dates are offered per price.</summary>
public class TourPrice : AuditableEntity, IAggregateRoot
{
    private TourPrice() { }

    public static TourPrice Create(DefaultIdType tourId, TourPriceDetails details)
    {
        var price = new TourPrice { TourId = tourId };
        price.Apply(details);
        return price;
    }

    public decimal Price { get; set; }
    public DefaultIdType TourId { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string? MonthFrom { get; set; }
    public string? MonthTo { get; set; }
    public decimal? DayDuration { get; set; }
    public decimal? NightDuration { get; set; }
    public decimal? HourDuration { get; set; }
    public decimal? ComissionAmount { get; private set; }
    public int? CommissionMinimumPax { get; private set; }
    public CommissionMinimumPaxScope? CommissionMinimumPaxScope { get; private set; }
    public bool? PublishToWebsite { get; private set; }
    public bool? InHouseOnly { get; private set; }

    public void Apply(TourPriceDetails details)
    {
        Price = details.Price;
        Title = details.Title.Trim();
        Description = details.Description;
        MonthFrom = details.MonthFrom;
        MonthTo = details.MonthTo;
        DayDuration = details.DayDuration;
        NightDuration = details.NightDuration;
        HourDuration = details.HourDuration;
        ComissionAmount = details.CommissionAmount;
        CommissionMinimumPax = details.CommissionMinimumPax;
        // A scope only means something alongside a minimum.
        CommissionMinimumPaxScope = details.CommissionMinimumPax is null ? null : details.CommissionMinimumPaxScope ?? Tours.CommissionMinimumPaxScope.PerDeparture;
        PublishToWebsite = details.PublishToWebsite;
        InHouseOnly = details.InHouseOnly;
    }

    /// <summary>When a departure at <paramref name="start"/> on this price ends (see <see cref="TourDurations.EndFor"/>).</summary>
    public DateTime EndFor(DateTime start) => TourDurations.EndFor(start, DayDuration, NightDuration, HourDuration);
}
