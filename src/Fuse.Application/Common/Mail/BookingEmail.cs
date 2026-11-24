namespace Fuse.Application.Common.Mail;

/// <summary>What the booking emails show (loaded before rendering: templates get no database).</summary>
public sealed record BookingEmail
{
    public DefaultIdType BookingId { get; init; }
    public int Reference { get; init; }
    public string GuestName { get; init; } = "";
    public string? GuestEmail { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal? AmountOutstanding { get; init; }
    public IReadOnlyList<BookingEmailItem> Items { get; init; } = [];

    /// <summary>
    /// Each line at its total (per guest × guests), as legacy's website confirmation (from its basket). Legacy's emails
    /// from the booking (payment reminder, payment-link and webhook confirmations) showed the per-guest price.
    /// </summary>
    public bool LineTotals { get; init; }

    /// <summary>What's owed now: the outstanding balance, else the total.</summary>
    public decimal AmountDue => AmountOutstanding is > 0 ? AmountOutstanding.Value : TotalAmount;
}

public sealed record BookingEmailItem
{
    public string TourName { get; init; } = "";
    public DateTime StartDate { get; init; }
    public int GuestCount { get; init; }
    /// <summary>Per guest.</summary>
    public decimal Amount { get; init; }
    public decimal LineTotal => Amount * GuestCount;
    public string? PickupLocation { get; init; }
    public string? SupplierId { get; init; }
    /// <summary>The tour's "Booking confirmation email" text (HTML).</summary>
    public string? ConfirmationDetails { get; init; }
    /// <summary>Everyone on the tour, lead guest first.</summary>
    public IReadOnlyList<string> GuestNames { get; init; } = [];
}

/// <summary>Renders the booking emails (legacy templates, branded from Mail:Brand).</summary>
public interface IBookingEmails
{
    Task<EmailMessage> PaymentReminderAsync(BookingEmail booking, string to, string checkoutUrl);
    Task<EmailMessage> ConfirmationAsync(BookingEmail booking, string to);
    Task<EmailMessage> SupplierConfirmationAsync(BookingEmail booking, string supplierId, string to, SupplierNotice notice = SupplierNotice.Confirmed);

    /// <summary>Legacy "Today's Tour Manifests": a departure's manifest for a hostel, 1 hour before it leaves.</summary>
    Task<EmailMessage> TourManifestAsync(TourManifest manifest, string to);
}

/// <summary>What a supplier's email tells them about their tours on a booking.</summary>
public enum SupplierNotice
{
    /// <summary>Legacy's "New Booking Confirmed".</summary>
    Confirmed,
    /// <summary>The tours were cancelled: the guests aren't coming.</summary>
    Cancelled,
    /// <summary>A cancelled booking was un-cancelled: the guests are coming after all.</summary>
    Reinstated,
}

/// <summary>What the manifest email says about a departure (the spreadsheet is attached).</summary>
public sealed record TourManifest(string TourName, DateTime StartDate, int Guests, int PaidGuests, int UnpaidGuests, FileDownload Spreadsheet);
