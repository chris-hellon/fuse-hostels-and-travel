using Fuse.Contracts.Bookings;

namespace Fuse.Admin.Pages.Bookings;

/// <summary>
/// The booking being created or edited. Every change (tours, packages, guests, the pack) stays here
/// until Save sends it in one <see cref="SaveBooking"/>. Prices are previewed with the same
/// <see cref="BookingPricing"/> rules the server applies.
/// </summary>
public sealed class BookingDraft(BookingEditor editor, BookingCatalog catalog, BookingAbilities abilities)
{
    public BookingEditor Editor { get; } = editor;
    public BookingCatalog Catalog { get; } = catalog;
    public BookingAbilities Abilities { get; } = abilities;

    public bool IsCreate => Editor.Id is null;

    /// <summary>Paid, refunded or cancelled: the details are read only (legacy FormDisabled).</summary>
    public bool Locked => !IsCreate && (Editor.IsPaid || Editor.Refunded || Editor.Cancelled);

    /// <summary>
    /// As legacy: tours, packages and the pack can be added, removed and changed only while the booking is open
    /// (not paid, refunded or cancelled), by anyone, administrators included.
    /// </summary>
    public bool CanChangeTours => !Editor.Cancelled && !Locked;

    /// <summary>As legacy: a booked tour (not a package's) can be edited on any booking that isn't cancelled.</summary>
    public bool CanEditTours => !Editor.Cancelled;

    /// <summary>As legacy BookingItemDialog: once paid or refunded, only administrators may change a tour's tour or its guests.</summary>
    public bool CanChangeTourAndGuests => !Editor.Cancelled && (!Locked || Abilities.IsAdmin);

    /// <summary>As legacy BookingItemDialog: a tour's departure (price and date) and pick up can change until the booking is refunded.</summary>
    public bool CanChangeDeparture => !Editor.Cancelled && !Editor.Refunded;

    /// <summary>The primary guest can change on an open booking, or by staff who may change who it's credited to.</summary>
    public bool CanChangeGuest => !Editor.Cancelled && (!Locked || Abilities.CanChangeSalesPerson);

    /// <summary>As legacy: any new booking; later only for a few staff, or administrators within an hour of booking.</summary>
    public bool CanChangePaymentMethod => IsCreate || Abilities.CanFlagAsPaid || (Abilities.IsAdmin && Editor.BookingDate >= DateTime.Now.AddHours(-1));

    /// <summary>The pack the booking is marked for (its saved pack even if it's since been switched off).</summary>
    public PackOption? Pack => Editor.PackIncentiveId is { } id
        ? Catalog.Packs.FirstOrDefault(p => p.Id == id) ?? (Editor.SavedPack?.Id == id ? Editor.SavedPack : null)
        : null;

    public BookableTour? Tour(DefaultIdType id) => Catalog.Tours.FirstOrDefault(t => t.Id == id);

    public PackageOption? Package(DefaultIdType id) => Catalog.Packages.FirstOrDefault(p => p.Id == id);

    /// <summary>The highest pack the booking's tours qualify for (Pack 4 over Pack 3), when none is applied.</summary>
    public PackOption? SuggestedPack => Editor.PackIncentiveId is not null ? null
        : Catalog.Packs.Where(p => p.MinimumTours > 0 && BookingPricing.Qualifies(p, Editor.Lines)).MaxBy(p => p.MinimumTours);

    /// <summary>Packages that still have tours on the booking.</summary>
    public IEnumerable<BookingPackageLine> Packages => Editor.Packages.Where(p => Editor.Lines.Any(l => l.PackageKey == p.Key));

    public string? PackageName(BookingLine line) => line.PackageKey is { } key ? Editor.Packages.FirstOrDefault(p => p.Key == key)?.PackageName : null;

    public void ApplyPack(PackOption pack, DefaultIdType? tierId)
    {
        Editor.PackIncentiveId = pack.Id;
        Editor.PackIncentiveTierId = tierId ?? pack.Tiers.FirstOrDefault()?.Id;
        Reprice();
    }

    public void SetPackTier(DefaultIdType? tierId)
    {
        Editor.PackIncentiveTierId = tierId;
        Reprice();
    }

    public void RemovePack()
    {
        Editor.PackIncentiveId = null;
        Editor.PackIncentiveTierId = null;
        Reprice();
    }

    public void Add(IEnumerable<BookingLine> lines)
    {
        Editor.Lines.AddRange(lines);
        Reprice();
    }

    public void Remove(BookingLine line)
    {
        Editor.Lines.Remove(line);
        Reprice();
    }

    /// <summary>Adds a package's tours, or replaces them when editing a package already on the booking.</summary>
    public void SetPackage(BookingPackageLine package, IEnumerable<BookingLine> lines)
    {
        Editor.Packages.RemoveAll(p => p.Key == package.Key);
        Editor.Lines.RemoveAll(l => l.PackageKey == package.Key);
        Editor.Packages.Add(package);
        Editor.Lines.AddRange(lines);
        Reprice();
    }

    public void RemovePackage(BookingPackageLine package)
    {
        Editor.Lines.RemoveAll(l => l.PackageKey == package.Key);
        Editor.Packages.Remove(package);
        Reprice();
    }

    public void Remember(string guestId, GuestName name) => Editor.GuestNames[guestId] = name;

    public GuestName NameOf(string guestId) => Editor.GuestNames.TryGetValue(guestId, out var name) ? name : new GuestName("Guest", null);

    /// <summary>Re-runs the pricing rules (after any change to tours, packages or the pack).</summary>
    public void Reprice()
    {
        var pack = Pack;
        // A pack that no longer qualifies stays marked (with a warning) but prices nothing until it does.
        BookingPricing.Apply(Editor.Lines,
            pack is not null && BookingPricing.Qualifies(pack, Editor.Lines) ? pack : null,
            Editor.PackIncentiveTierId, Editor.Packages, Catalog.Packages);
    }

    /// <summary>A new line for a departure (the lead guest is implied; additional guests as given).</summary>
    public BookingLine NewLine(BookableTour tour, DepartureOption departure, string? pickup, IEnumerable<string> guestIds, DefaultIdType? packageKey = null) => new()
    {
        Key = DefaultIdType.NewGuid(),
        IsNew = true,
        TourId = tour.Id,
        TourName = tour.Name,
        TourDateId = departure.Id,
        PriceTitle = departure.PriceTitle,
        StartDate = departure.StartDate,
        EndDate = departure.EndDate,
        DeparturePrice = departure.Price,
        Amount = departure.Price,
        PickupLocation = pickup,
        PackageKey = packageKey,
        GuestIds = [.. guestIds],
    };
}
