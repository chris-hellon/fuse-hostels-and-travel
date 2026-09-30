namespace Fuse.Application.Features.Bookings;

/// <summary>The "Bookings" configuration section.</summary>
public sealed class BookingOptions
{
    public const string Section = "Bookings";

    /// <summary>
    /// Staff who may mark a booking paid without a payment, and change a booking's payment method.
    /// Legacy hard-coded these user ids in the bookings page.
    /// </summary>
    public List<string> FlagAsPaidUserIds { get; set; } = [];
}
