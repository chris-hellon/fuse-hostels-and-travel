using System.Text.Json;
using Fuse.Contracts.Bookings;
using Fuse.Contracts.Dashboard;

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

    /// <summary>A hostel stay the booking is for (a new booking started from a hostel guest).</summary>
    public sealed record GuestStay(string GuestId, string? Hostel, DateOnly CheckIn, DateOnly CheckOut);

    /// <summary>
    /// The stay a new booking was started from: while its guest is still the primary guest, Add Tours offers departures
    /// during it first and picks up from its hostel.
    /// </summary>
    public GuestStay? Stay { get; set; }

    /// <summary><see cref="Stay"/>, while it's still the primary guest's.</summary>
    public GuestStay? PrimaryGuestStay => Stay is { } stay && string.Equals(stay.GuestId, Editor.GuestId, StringComparison.OrdinalIgnoreCase) ? stay : null;

    /// <summary>Changes this wizard saved straight away (a tour refunded or cancelled) not yet taken into <see cref="BookingEditor.ConcurrencyVersion"/>.</summary>
    private int _ownChanges;

    /// <summary>The booking's version if nobody else has changed it: the draft's, plus each change this wizard saved itself.</summary>
    public int ExpectedVersion => Editor.ConcurrencyVersion + _ownChanges;

    /// <summary>This wizard saved a change to the booking straight away (each one moves its version on by one).</summary>
    public void SavedOwnChange() => _ownChanges++;

    private string _original = "";

    /// <summary>Whether anything was changed since the booking was opened (or taken from the server again).</summary>
    public bool HasChanges => JsonSerializer.Serialize(Editor) != _original;

    /// <summary>What's in the draft now is the booking as saved (after loading and pricing it).</summary>
    public void MarkUnchanged() => _original = JsonSerializer.Serialize(Editor);

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

    /// <summary>
    /// As legacy: a saved tour that's paid (not Unpaid) and not cancelled can be refunded or cancelled on its own
    /// straight away, by staff who may refund. New tours can't (they aren't on the booking until Save).
    /// </summary>
    public bool CanRefundOrCancel(BookingLine line) =>
        Abilities.CanRefund && !Editor.Cancelled && !line.IsNew && !line.Cancelled
        && line.SavedStatus is { } status && status != BookingState.Unpaid;

    /// <summary>
    /// After a tour was refunded or cancelled on the server: takes what that changed from the booking as it's now
    /// saved, keeping every other unsaved change in the draft. The booking's refund / cancel / payment state and
    /// each saved tour's refund state and status are refreshed; a tour cancelled there comes back exactly as saved
    /// (a cancelled tour doesn't change on Save, so edits to it would be lost anyway). The saved concurrency version is
    /// taken only when it's exactly <see cref="ExpectedVersion"/> (only this wizard's own changes since it opened);
    /// otherwise the draft keeps its own, so someone else's change in the meantime still stops the Save.
    /// </summary>
    public void ApplySavedState(BookingEditor saved)
    {
        var unchanged = !HasChanges;
        if (saved.ConcurrencyVersion == ExpectedVersion)
        {
            Editor.ConcurrencyVersion = saved.ConcurrencyVersion;
            _ownChanges = 0;
        }
        Editor.IsPaid = saved.IsPaid;
        Editor.Refunded = saved.Refunded;
        Editor.Cancelled = saved.Cancelled;
        Editor.SavedTotal = saved.SavedTotal;
        Editor.AmountOutstanding = saved.AmountOutstanding;
        foreach (var (id, name) in saved.GuestNames)
            Editor.GuestNames.TryAdd(id, name);

        for (var i = 0; i < Editor.Lines.Count; i++)
        {
            var line = Editor.Lines[i];
            if (line.IsNew || saved.Lines.FirstOrDefault(l => l.Key == line.Key) is not { } fresh)
                continue;
            if (fresh.Cancelled && !line.Cancelled)
            {
                Editor.Lines[i] = fresh;
                continue;
            }
            line.Refunded = fresh.Refunded;
            line.SavedStatus = fresh.SavedStatus;
        }
        Reprice();
        if (unchanged)
            MarkUnchanged();
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
