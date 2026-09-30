using System.Text.Json;
using Fuse.Site.Interop;

namespace Fuse.Site.Components.Booking;

/// <summary>
/// The visitor's basket (legacy's session basket): the tour departures they mean to book, kept in the browser until
/// checkout. Owned by the layout and cascaded to pages. Prices here are for show; checkout prices on the server.
/// </summary>
public sealed class Basket
{
    private const string StorageKey = "fuse-basket";

    private List<BasketItem> _items = [];
    private FuseJs? _js;

    public IReadOnlyList<BasketItem> Items => _items;

    /// <summary>The booking made at checkout and waiting for payment (replaced if they check out again).</summary>
    public DefaultIdType? PendingBookingId { get; private set; }

    /// <summary>
    /// The staff member whose booking QR code a tour was added from (legacy's staff booking link): checkout credits
    /// the booking to them, and prices the basket as they would (tours shown only by QR code, prices not published to the website).
    /// </summary>
    public DefaultIdType? StaffId { get; private set; }
    public int Count => _items.Count;
    public decimal Total => _items.Sum(i => i.Total);

    /// <summary>False until read from the browser (never while prerendering).</summary>
    public bool Loaded { get; private set; }

    public event Action? Changed;

    internal async Task LoadAsync(FuseJs js)
    {
        _js = js;
        var json = await js.InvokeAsync<string?>("readStorage", StorageKey);
        try
        {
            var stored = string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<Stored>(json);
            (_items, PendingBookingId, StaffId) = (stored?.Items ?? [], stored?.PendingBookingId, stored?.StaffId);
        }
        catch (JsonException)
        {
            (_items, PendingBookingId, StaffId) = ([], null, null); // an older or damaged basket
        }
        Loaded = true;
        Changed?.Invoke();
    }

    /// <summary>Adds a departure, or (as legacy) updates it if it's already in the basket. From a staff booking link,
    /// the whole basket is then credited to that staff member.</summary>
    public Task AddAsync(BasketItem item, DefaultIdType? staffId = null)
    {
        StaffId = staffId ?? StaffId;
        var at = _items.FindIndex(i => i.DepartureId == item.DepartureId);
        if (at >= 0)
            _items[at] = item;
        else
            _items.Add(item);
        return SaveAsync();
    }

    public Task RemoveAsync(DefaultIdType departureId)
    {
        _items.RemoveAll(i => i.DepartureId == departureId);
        return SaveAsync();
    }

    /// <summary>Paid: nothing left to book.</summary>
    public Task ClearAsync()
    {
        _items.Clear();
        (PendingBookingId, StaffId) = (null, null);
        return SaveAsync();
    }

    public Task SetPendingBookingAsync(DefaultIdType bookingId)
    {
        PendingBookingId = bookingId;
        return SaveAsync();
    }

    public BasketItem? Find(DefaultIdType departureId) => _items.FirstOrDefault(i => i.DepartureId == departureId);

    /// <summary>A tour's page, through the staff booking link while the basket has one (a tour shown only by QR code
    /// has no public page).</summary>
    public string TourLink(string slug) => StaffId is { } staff ? $"/tours/staff-booking/{slug}/{staff}" : $"/tours/{slug}";

    private async Task SaveAsync()
    {
        Changed?.Invoke();
        if (_js is not null)
            await _js.InvokeVoidAsync("writeStorage", StorageKey,
                _items.Count == 0 && PendingBookingId is null ? null : JsonSerializer.Serialize(new Stored(_items, PendingBookingId, StaffId)));
    }

    private sealed record Stored(List<BasketItem> Items, DefaultIdType? PendingBookingId, DefaultIdType? StaffId = null);
}

/// <summary>A departure in the basket, with what the basket shows about it.</summary>
public sealed record BasketItem
{
    public DefaultIdType TourId { get; init; }
    public string TourName { get; init; } = "";
    public string TourSlug { get; init; } = "";
    public string? ImagePath { get; init; }
    public DefaultIdType DepartureId { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    public string PriceTitle { get; init; } = "";
    /// <summary>Per guest, when added (checkout uses the current price).</summary>
    public decimal UnitPrice { get; init; }
    public int Guests { get; init; }
    public string? Pickup { get; init; }

    public decimal Total => UnitPrice * Guests;
}
