using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Fuse.Application.Common.Mail;
using Fuse.Infrastructure.Mail.Templates;

namespace Fuse.Infrastructure.Mail;

/// <summary>Renders the booking email components to HTML (Blazor's HtmlRenderer, no browser needed).</summary>
internal sealed class BookingEmails(IServiceProvider services, ILoggerFactory logs, IOptions<MailBrand> brand) : IBookingEmails
{
    public async Task<EmailMessage> PaymentReminderAsync(BookingEmail booking, string to, string checkoutUrl) =>
        new([to], $"Payment reminder for booking {booking.Reference}", await RenderAsync<PaymentReminder>(new()
        {
            [nameof(PaymentReminder.Brand)] = brand.Value,
            [nameof(PaymentReminder.Booking)] = booking,
            [nameof(PaymentReminder.CheckoutUrl)] = checkoutUrl,
        }));

    public async Task<EmailMessage> ConfirmationAsync(BookingEmail booking, string to) =>
        new([to], $"{brand.Value.TenantName} Order Confirmation", await RenderAsync<BookingConfirmation>(new()
        {
            [nameof(BookingConfirmation.Brand)] = brand.Value,
            [nameof(BookingConfirmation.Booking)] = booking,
        }));

    public async Task<EmailMessage> SupplierConfirmationAsync(BookingEmail booking, string supplierId, string to, SupplierNotice notice = SupplierNotice.Confirmed) =>
        new([to], notice switch
        {
            SupplierNotice.Cancelled => $"{brand.Value.TenantName} Booking Cancelled - {booking.Reference}",
            SupplierNotice.Reinstated => $"{brand.Value.TenantName} Booking Reinstated - {booking.Reference}",
            _ => $"{brand.Value.TenantName} Booking Confirmation",
        }, await RenderAsync<SupplierBookingConfirmation>(new()
        {
            [nameof(SupplierBookingConfirmation.Brand)] = brand.Value,
            [nameof(SupplierBookingConfirmation.Booking)] = booking,
            [nameof(SupplierBookingConfirmation.SupplierId)] = supplierId,
            [nameof(SupplierBookingConfirmation.Notice)] = notice,
        }));

    public async Task<EmailMessage> TourManifestAsync(TourManifest manifest, string to) =>
        new([to], $"Today's Tour Manifests for {manifest.TourName} - {manifest.StartDate:HH:mm}", await RenderAsync<ManifestEmail>(new()
        {
            [nameof(ManifestEmail.Brand)] = brand.Value,
            [nameof(ManifestEmail.Manifest)] = manifest,
        }))
        {
            Attachments = [new EmailAttachment(manifest.Spreadsheet.FileName, manifest.Spreadsheet.ContentType, manifest.Spreadsheet.Content)],
        };

    private Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters) where TComponent : IComponent =>
        EmailRenderer.RenderAsync<TComponent>(services, logs, parameters);
}
