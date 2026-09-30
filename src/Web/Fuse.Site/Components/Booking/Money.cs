using System.Globalization;

namespace Fuse.Site.Components.Booking;

/// <summary>Prices as the site shows them (legacy: "$" and the amount to the cent, e.g. "$29.20").</summary>
public static class Money
{
    public static string Usd(decimal amount) => "$" + amount.ToString("0.00", CultureInfo.InvariantCulture);
}
