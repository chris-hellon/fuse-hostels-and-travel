using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Application.Common.Mail;

namespace Fuse.Web.Tests;

/// <summary>The booking emails show each tour's date and departure time as legacy did (StartDate.ToLongDateString() under
/// the app culture, whose long date pattern was "dd/MM/yyyy hh:mm:ss tt").</summary>
public sealed class BookingEmailDateTests(TestApp app)
{
    private static BookingEmail Booking => new()
    {
        Reference = 12345,
        GuestName = "Sam Walker",
        GuestEmail = "sam@tests.invalid",
        TotalAmount = 50,
        Items =
        [
            new BookingEmailItem { TourName = "Morning Tour", StartDate = new DateTime(2026, 9, 26, 8, 0, 0), GuestCount = 2, Amount = 25, SupplierId = "supplier" },
            new BookingEmailItem { TourName = "Sunset Tour", StartDate = new DateTime(2026, 9, 27, 14, 30, 0), GuestCount = 1, Amount = 25, SupplierId = "supplier" },
        ],
    };

    [Fact]
    public async Task Confirmations_and_reminders_show_the_tour_date_with_its_start_time()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var emails = scope.ServiceProvider.GetRequiredService<IBookingEmails>();

        foreach (var email in new[]
                 {
                     await emails.ConfirmationAsync(Booking, "sam@tests.invalid"),
                     await emails.SupplierConfirmationAsync(Booking, "supplier", "supplier@tests.invalid"),
                     await emails.PaymentReminderAsync(Booking, "sam@tests.invalid", "https://tests.invalid/pay"),
                 })
        {
            email.HtmlBody.ShouldContain("2 guests at Morning Tour on 26/09/2026 08:00:00 AM");
            email.HtmlBody.ShouldContain("1 guest at Sunset Tour on 27/09/2026 02:30:00 PM");
        }
    }
}
