using System.Globalization;

namespace Fuse.Infrastructure.Mail.Templates;

/// <summary>The legacy templates' inline styles (email clients ignore stylesheets).</summary>
internal static class EmailStyles
{
    private const string Font = "font-family: Open Sans, Helvetica, Arial, sans-serif; font-size: 16px; line-height: 24px;";
    public const string Body = Font + " font-weight: 400;";
    public const string Paragraph = "font-size: 16px; font-weight: 400; line-height: 24px; color: #777777;";
    public const string HeaderCell = Font + " font-weight: 800; padding: 10px;";
    public const string ItemCell = Body + " padding: 15px 10px 5px 10px;";
    public const string TotalCell = Font + " font-weight: 800; padding: 10px; border-top: 3px solid #eeeeee; border-bottom: 3px solid #eeeeee;";

    public static string Money(decimal amount) => $"${amount:n2}";

    /// <summary>Legacy's StartDate.ToLongDateString() under the app culture (LongDatePattern "dd/MM/yyyy hh:mm:ss tt"):
    /// the date and the departure time, e.g. "26/09/2026 08:00:00 AM".</summary>
    public static string Date(DateTime start) => start.ToString("dd/MM/yyyy hh:mm:ss tt", CultureInfo.GetCultureInfo("en-US"));

    public static string Line(Application.Common.Mail.BookingEmailItem item) =>
        $"{item.GuestCount} guest{(item.GuestCount > 1 ? "s" : "")} at {item.TourName} on {Date(item.StartDate)}";
}
