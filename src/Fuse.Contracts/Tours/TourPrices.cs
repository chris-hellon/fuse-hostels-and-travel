namespace Fuse.Contracts.Tours;

public enum CommissionScope
{
    PerDeparture = 0,
    PerDay = 1,
}

public sealed record TourPriceInput
{
    public decimal Price { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? MonthFrom { get; set; }
    public string? MonthTo { get; set; }
    public decimal? DayDuration { get; set; }
    public decimal? NightDuration { get; set; }
    public decimal? HourDuration { get; set; }
    public decimal? CommissionAmount { get; set; }
    public int? CommissionMinimumPax { get; set; }
    public CommissionScope? CommissionScope { get; set; }
    public bool PublishToWebsite { get; set; }
    public bool InHouseOnly { get; set; }
}

/// <summary>
/// A price in the tour editor. <see cref="Key"/> is the price's id once saved; a new price gets a
/// key from the editor so its new dates can refer to it before it has an id.
/// </summary>
public sealed record TourPriceEdit
{
    public DefaultIdType Key { get; init; }
    public TourPriceInput Details { get; init; } = new();
}

public sealed class TourPriceInputValidator : AbstractValidator<TourPriceInput>
{
    public TourPriceInputValidator()
    {
        RuleFor(x => x.Price).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(256);
        RuleFor(x => x.DayDuration).GreaterThanOrEqualTo(0).WithName("Day Duration");
        RuleFor(x => x.NightDuration).GreaterThanOrEqualTo(0).WithName("Night Duration");
        RuleFor(x => x.HourDuration).GreaterThanOrEqualTo(0).WithName("Hour Duration");
        RuleFor(x => x.CommissionMinimumPax).GreaterThanOrEqualTo(1).When(x => x.CommissionMinimumPax.HasValue)
            .WithName("Commission minimum pax");
    }
}
