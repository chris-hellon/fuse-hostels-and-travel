using Fuse.Contracts.Tours;

namespace Fuse.Contracts.Bookings;

// What the wizard picks from ---------------------------------------------------------------------

public sealed record BookableTour
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    public DefaultIdType? CategoryId { get; init; }
    public bool WaiverRequired { get; init; }
    /// <summary>Pick up points (property names); "Other" is always offered too.</summary>
    public IReadOnlyList<string> Pickups { get; init; } = [];
}

public sealed record PackTierOption(DefaultIdType Id, string Title, decimal Price, int SortOrder);

/// <summary>A pack incentive: book at least <see cref="MinimumTours"/> of its tours and charge a tier price for them.</summary>
public sealed record PackOption
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    public int MinimumTours { get; init; }
    public IReadOnlyList<DefaultIdType> TourIds { get; init; } = [];
    public IReadOnlyList<PackTierOption> Tiers { get; init; } = [];
}

public sealed record PackageTierOption(DefaultIdType Id, string Title, int SortOrder);

public sealed record PackageItemOption
{
    public DefaultIdType Id { get; init; }
    public DefaultIdType TourId { get; init; }
    public string TourName { get; init; } = "";
    public int Quantity { get; init; } = 1;
    /// <summary>When set, the package books this price of the tour.</summary>
    public DefaultIdType? TourPriceId { get; init; }
    /// <summary>The tour's price in the package, per tier.</summary>
    public IReadOnlyDictionary<DefaultIdType, decimal> Prices { get; init; } = new Dictionary<DefaultIdType, decimal>();
}

public sealed record PackageOption
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    public string? ShortDescription { get; init; }
    public IReadOnlyList<PackageTierOption> Tiers { get; init; } = [];
    public IReadOnlyList<PackageItemOption> Items { get; init; } = [];

    public PackageTierOption? DefaultTier => Tiers.OrderBy(t => t.SortOrder).FirstOrDefault();

    /// <summary>Per guest: each tour's tier price times its quantity.</summary>
    public decimal TotalFor(DefaultIdType tierId) => Items.Sum(i => i.Prices.GetValueOrDefault(tierId) * i.Quantity);
}

public sealed record BookingCatalog
{
    public IReadOnlyList<BookableTour> Tours { get; init; } = [];
    public IReadOnlyList<LookupItem> Categories { get; init; } = [];
    public IReadOnlyList<string> Sources { get; init; } = [];
    public IReadOnlyList<PackOption> Packs { get; init; } = [];
    public IReadOnlyList<PackageOption> Packages { get; init; } = [];
}

/// <summary>Everything the booking wizard offers: tours (with pick ups), categories, sources, active packs and packages.</summary>
[Get("bookings/catalog"), Permission(FuseAction.View, FuseResource.TourBookings)]
public sealed record GetBookingCatalog : IQuery<BookingCatalog>;

/// <summary>A departure that can be booked (or is already on the booking).</summary>
public sealed record DepartureOption
{
    public DefaultIdType Id { get; init; }
    public DefaultIdType? PriceId { get; init; }
    public string PriceTitle { get; init; } = "";
    /// <summary>Per guest: the departure's price override, else its price.</summary>
    public decimal Price { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public int AvailableSpaces { get; init; }
}

/// <summary>
/// A tour's departures with space, soonest first: from today, or from a month ago for administrators
/// (to fix up recent bookings), as legacy.
/// </summary>
[Get("bookings/tours/{TourId}/departures"), Permission(FuseAction.View, FuseResource.TourBookings)]
public sealed record ListTourDepartures(DefaultIdType TourId) : IQuery<IReadOnlyList<DepartureOption>>;

/// <summary>A tour's waiver terms (shown while adding guests).</summary>
[Get("bookings/tours/{TourId}/waiver"), Permission(FuseAction.View, FuseResource.TourBookings)]
public sealed record GetTourWaiver(DefaultIdType TourId) : IQuery<string>;

/// <summary>Staff to credit with a booking ("Booked By"), by name or email.</summary>
[Get("bookings/staff"), Permission(FuseAction.View, FuseResource.TourBookings)]
public sealed record FindBookingStaff(string Search) : IQuery<IReadOnlyList<LookupItem>>;

// The booking being edited --------------------------------------------------------------------------

/// <summary>One tour on the booking. <see cref="Key"/> is the item's id (a new id for a tour being added).</summary>
public sealed record BookingLine
{
    public DefaultIdType Key { get; set; }
    public bool IsNew { get; set; }
    public DefaultIdType TourId { get; set; }
    public string TourName { get; set; } = "";
    public DefaultIdType TourDateId { get; set; }
    public string PriceTitle { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    /// <summary>The departure's own price per guest (what the tour costs without a pack or package).</summary>
    public decimal DeparturePrice { get; set; }
    /// <summary>What this tour costs per guest on the booking (worked out by <see cref="BookingPricing"/>).</summary>
    public decimal Amount { get; set; }
    /// <summary>
    /// A pack tour's exact share of the tier price (<see cref="Amount"/> is it to the cent, as stored). As legacy,
    /// the booking's total adds up the exact shares, so a $100 pack of three is 3 × $33.33 but totals $100.00.
    /// </summary>
    public decimal? PackShare { get; set; }
    public string? PickupLocation { get; set; }
    public bool WaiverSigned { get; set; }
    /// <summary>The package (<see cref="BookingPackageLine.Key"/>) this tour was booked in.</summary>
    public DefaultIdType? PackageKey { get; set; }
    /// <summary>Guests besides the lead guest (who goes on every tour).</summary>
    public List<string> GuestIds { get; set; } = [];
    public bool Cancelled { get; set; }
    public bool Refunded { get; set; }

    /// <summary>As saved: its price, departure, package and whether a pack or package set that price.</summary>
    public decimal? SavedAmount { get; set; }
    public DefaultIdType? SavedTourDateId { get; set; }
    public DefaultIdType? SavedPackageKey { get; set; }
    public bool SavedSpecialPrice { get; set; }

    public int GuestCount => GuestIds.Count + 1;
    public decimal Total => Amount * GuestCount;
}

/// <summary>A package booked on the booking: its tours share its <see cref="Key"/>.</summary>
public sealed record BookingPackageLine
{
    public DefaultIdType Key { get; set; }
    public DefaultIdType PackageId { get; set; }
    public string PackageName { get; set; } = "";
    public DefaultIdType? TierId { get; set; }
    public string? TierTitle { get; set; }
    /// <summary>Per guest, for the tier.</summary>
    public decimal Price { get; set; }
    /// <summary>The tier as saved (a package's tours keep their saved prices until its tier or departures change).</summary>
    public DefaultIdType? SavedTierId { get; set; }
    /// <summary>
    /// Picked (or picked again) in the package dialog during this edit. As legacy, only then is a booked package's
    /// snapshot (name, price, tier, commission) re-read from the package; otherwise it stays as booked.
    /// </summary>
    public bool Edited { get; set; }
}

/// <param name="Detail">Date of birth, gender, nationality and email, as the guest pickers show them.</param>
public sealed record GuestName(string Name, string? Detail);

public sealed record BookingEditor
{
    public DefaultIdType? Id { get; set; }
    public int Reference { get; set; }
    public int ConcurrencyVersion { get; set; }
    public DateTime BookingDate { get; set; }
    public string Description { get; set; } = "";
    public string? GuestId { get; set; }
    public string? GuestName { get; set; }
    public string? BookingSource { get; set; }
    public string? AdditionalNotes { get; set; }
    public BookingPaymentMethod PaymentMethod { get; set; } = BookingPaymentMethod.Stripe;
    public string? BookedById { get; set; }
    public string? BookedByName { get; set; }
    /// <summary>The guest booked on the website ("Booked By" can't change).</summary>
    public bool DirectFromWebsite { get; set; }
    public DefaultIdType? PackIncentiveId { get; set; }
    public DefaultIdType? PackIncentiveTierId { get; set; }
    public List<BookingPackageLine> Packages { get; set; } = [];
    public List<BookingLine> Lines { get; set; } = [];
    /// <summary>Names (and a detail line) of the guests on the tours, by id, for display.</summary>
    public Dictionary<string, GuestName> GuestNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // As saved (read only in the wizard).
    public bool IsPaid { get; set; }
    public bool Refunded { get; set; }
    public bool Cancelled { get; set; }
    public decimal SavedTotal { get; set; }
    public decimal? AmountOutstanding { get; set; }
    public DefaultIdType? SavedPackIncentiveId { get; set; }
    public DefaultIdType? SavedPackIncentiveTierId { get; set; }
    /// <summary>The booking's pack as saved, even if it's since been switched off.</summary>
    public PackOption? SavedPack { get; set; }

    public decimal Total => BookingPricing.Total(Lines);
}

[Get("bookings/{Id}"), Permission(FuseAction.View, FuseResource.TourBookings)]
public sealed record GetBookingEditor(DefaultIdType Id) : IQuery<BookingEditor>;

/// <summary>What to do when saving changes the total of a booking that's already paid.</summary>
public enum PaidTotalChange
{
    /// <summary>The total didn't change (or the booking isn't paid).</summary>
    None,
    /// <summary>The guest owes the difference: the booking becomes unpaid for it.</summary>
    ChargeDifference,
    /// <summary>Keep the amount paid as the booking's total.</summary>
    KeepAmountPaid,
    /// <summary>Refund <see cref="SaveBooking.RefundAmount"/> and use the new total.</summary>
    Refund,
}

/// <summary>
/// Creates or updates a booking in one go: details, tours (added, changed, removed), packages and the pack
/// incentive. Prices come from the server (<see cref="BookingPricing"/>); spaces are taken and given back.
/// </summary>
[Post("bookings/save"), Permission(FuseAction.Update, FuseResource.Bookings)]
public sealed record SaveBooking(BookingEditor Booking, PaidTotalChange PaidChange = PaidTotalChange.None, decimal RefundAmount = 0) : ICommand<DefaultIdType>;

public sealed class SaveBookingValidator : AbstractValidator<SaveBooking>
{
    public SaveBookingValidator()
    {
        RuleFor(x => x.Booking.Description).NotEmpty().WithMessage("Please enter a Description.").MaximumLength(2000).WithName("Description");
        RuleFor(x => x.Booking.GuestId).NotEmpty().WithMessage("Please select a Primary Guest.").WithName("Primary Guest");
        RuleFor(x => x.Booking.BookingSource).NotEmpty().WithMessage("Please select a Booking Source.").MaximumLength(128).WithName("Booking Source");
        RuleFor(x => x.Booking.AdditionalNotes).MaximumLength(2000).WithName("Additional Notes");
        RuleFor(x => x.Booking.Lines).NotEmpty().WithMessage("No Tours selected. Please add a Tour to continue.").WithName("Tours");
        RuleForEach(x => x.Booking.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.TourDateId).NotEmpty().WithMessage("Please select a Tour Date.");
            line.RuleFor(l => l.PickupLocation).MaximumLength(500);
        });
        RuleFor(x => x.RefundAmount).GreaterThanOrEqualTo(0);
    }
}

/// <summary>
/// Prices on a booking, shared by the wizard (as a preview) and the server (which decides):
/// <list type="number">
/// <item>A pack incentive the booking qualifies for charges its tier price, split evenly across the pack's
/// tours on the booking (packages and cancelled tours included, as legacy: the pack wins).</item>
/// <item>A package's tours cost their price in the package for its tier (a booked package's tours keep their
/// saved price until its tier or their departure changes, as legacy).</item>
/// <item>A saved tour keeps its saved price while its departure (and whether a pack or package priced it) is unchanged.</item>
/// <item>Anything else costs its departure's price.</item>
/// </list>
/// </summary>
public static class BookingPricing
{
    /// <summary>How many different pack tours are on the booking (cancelled tours count too, as legacy).</summary>
    public static int EligibleTours(PackOption pack, IEnumerable<BookingLine> lines) =>
        lines.Where(l => pack.TourIds.Contains(l.TourId)).Select(l => l.TourId).Distinct().Count();

    public static bool Qualifies(PackOption pack, IEnumerable<BookingLine> lines) => EligibleTours(pack, lines) >= pack.MinimumTours;

    /// <summary>
    /// To the cent, as SQL Server stores legacy's unrounded amounts (decimal(18,2) rounds half away from zero).
    /// </summary>
    public static decimal Cents(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>What a pack tour costs as stored (legacy PackIncentivePricing.PerTourAmount, to the cent).</summary>
    public static decimal PackPrice(decimal tierPrice, int packTours) => Cents(tierPrice / packTours);

    /// <summary>
    /// The booking's total as legacy stored it: every tour's price × its guests, pack tours at their exact share,
    /// then to the cent. A $100 pack of three tours is $100.00 although each tour is $33.33.
    /// </summary>
    public static decimal Total(IEnumerable<BookingLine> lines) => Cents(lines.Sum(l => (l.PackShare ?? l.Amount) * l.GuestCount));

    /// <summary>Sets every line's <see cref="BookingLine.Amount"/>.</summary>
    /// <param name="pack">The pack the booking is marked for (null for none).</param>
    /// <param name="packTierId">The pack's tier.</param>
    /// <param name="packageLines">The booking's packages (for their tiers).</param>
    /// <param name="packages">The package definitions (for their tours' prices).</param>
    public static void Apply(IList<BookingLine> lines, PackOption? pack, DefaultIdType? packTierId,
        IReadOnlyList<BookingPackageLine> packageLines, IReadOnlyList<PackageOption> packages)
    {
        var tier = pack?.Tiers.FirstOrDefault(t => t.Id == packTierId);
        var packLines = pack is not null && tier is not null && Qualifies(pack, lines)
            ? lines.Where(l => pack.TourIds.Contains(l.TourId)).ToList() // cancelled tours keep their share, as legacy
            : [];

        foreach (var line in lines)
        {
            line.PackShare = null;
            if (packLines.Contains(line))
            {
                // As legacy: the item stores its share to the cent; the total adds up the exact shares.
                line.PackShare = tier!.Price / packLines.Count;
                line.Amount = Cents(line.PackShare.Value);
                continue;
            }

            var sameDeparture = !line.IsNew && line.SavedAmount is not null && line.SavedTourDateId == line.TourDateId;
            var packageLine = line.PackageKey is { } key ? packageLines.FirstOrDefault(p => p.Key == key) : null;
            if (packageLine is not null)
            {
                // As legacy: a booked package's tours keep their price until its tier or their departure changes.
                if (sameDeparture && line.SavedPackageKey == packageLine.Key && packageLine.TierId == packageLine.SavedTierId)
                {
                    line.Amount = line.SavedAmount!.Value;
                    continue;
                }
                var item = packages.FirstOrDefault(p => p.Id == packageLine.PackageId)?.Items.FirstOrDefault(i => i.TourId == line.TourId);
                if (item is not null && packageLine.TierId is { } packageTier)
                {
                    line.Amount = item.Prices.GetValueOrDefault(packageTier);
                    continue;
                }
            }

            line.Amount = sameDeparture && !line.SavedSpecialPrice ? line.SavedAmount!.Value : line.DeparturePrice;
        }
    }
}

public static class GuestNames
{
    /// <summary>"01/10/2002 · Female · United Kingdom · caitlin@…" (legacy guest picker line).</summary>
    public static GuestName Of(string name, DateTime? dateOfBirth, string? gender, string? nationality, string? email)
    {
        var parts = new[]
        {
            dateOfBirth?.ToString("dd/MM/yyyy"),
            Shared.Globalization.Genders.DisplayName(gender),
            Shared.Globalization.Countries.DisplayName(nationality),
            email,
        };
        var detail = string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return new GuestName(name, detail.Length == 0 ? null : detail);
    }
}
