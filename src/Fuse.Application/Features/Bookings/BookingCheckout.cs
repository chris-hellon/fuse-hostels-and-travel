using Microsoft.Extensions.Options;
using Fuse.Application.Common.Jobs;
using Fuse.Application.Common.Mail;
using Fuse.Application.Common.Payments;
using Fuse.Application.Common.Time;
using Fuse.Application.Features.Identity;
using Fuse.Application.Features.Tours;
using Fuse.Contracts.Bookings;
using Fuse.Domain.Catalog.Bookings;

namespace Fuse.Application.Features.Bookings;

internal static class BookingEmailLoader
{
    /// <summary>A booking as the emails show it: its (not cancelled) tours, guests, supplier and confirmation text.</summary>
    public static async Task<BookingEmail> LoadAsync(IAppDbContext db, DefaultIdType bookingId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.AsNoTracking().AsSplitQuery()
            .Include(b => b.Items).ThenInclude(i => i.Guests)
            .Include(b => b.Items).ThenInclude(i => i.Tour)
            .FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking not found.");
        var items = booking.Items.Where(i => i.TourId != null && i.Cancelled != true).OrderBy(i => i.StartDate).ToList();
        var guestIds = items.SelectMany(i => i.Guests ?? []).Where(g => g.DeletedOn == null).Select(g => g.GuestId.ToString()).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => guestIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.FullName ?? (u.FirstName + " " + u.LastName) })
            .ToDictionaryAsync(u => u.Id, u => u.Name, StringComparer.OrdinalIgnoreCase, cancellationToken);

        return new BookingEmail
        {
            BookingId = booking.Id,
            Reference = booking.InvoiceId,
            GuestName = booking.GuestName ?? "",
            GuestEmail = PlaceholderEmails.Visible(booking.GuestEmail),
            TotalAmount = booking.TotalAmount,
            AmountOutstanding = booking.AmountOutstanding,
            Items =
            [
                .. items.Select(i => new BookingEmailItem
                {
                    TourName = i.Tour?.Name ?? "",
                    StartDate = i.StartDate,
                    GuestCount = BookingStatuses.GuestCount(i),
                    Amount = i.Amount,
                    PickupLocation = i.PickupLocation,
                    SupplierId = i.Tour?.SupplierId,
                    ConfirmationDetails = i.Tour?.BookingConfirmationEmailDetails,
                    GuestNames =
                    [
                        booking.GuestName ?? "Guest",
                        .. (i.Guests ?? []).Where(g => g.DeletedOn == null).Select(g => names.GetValueOrDefault(g.GuestId.ToString()) ?? "Guest"),
                    ],
                }),
            ],
        };
    }
}

/// <summary>How a checkout is made beyond legacy's defaults.</summary>
/// <param name="WebsiteCheckout">The website's own checkout (copies the team on its confirmation, as legacy).</param>
/// <param name="TestMode">Stripe's test mode (the public API's test bookings).</param>
/// <param name="SuccessUrl">Where Stripe returns after paying, instead of the site's confirmation page.</param>
/// <param name="CancelUrl">Where Stripe returns on cancel, instead of the site's order-failed page.</param>
/// <param name="RecordSession">As legacy's admin payment links and API: the booking remembers the checkout it was sent.</param>
internal sealed record CheckoutOptions(bool WebsiteCheckout = false, bool TestMode = false, string? SuccessUrl = null, string? CancelUrl = null,
    bool RecordSession = false);

/// <summary>
/// Stripe checkouts for what a booking owes, made as legacy made them (CreateStripeQrCodeRequest, CreateStripeSessionRequest,
/// CreateApiStripeCheckoutRequest): one line per tour at its total, "2 guests at TOUR on Saturday, September 26, 2026", the
/// tour's photo, and the guest's existing Stripe customer when they have one.
/// </summary>
internal sealed class BookingCheckouts(IAppDbContext db, IPaymentGateway payments)
{
    private static readonly System.Globalization.CultureInfo LegacyCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");

    public async Task<(CheckoutSession Session, decimal Amount)> CreateAsync(Booking booking, CancellationToken cancellationToken, CheckoutOptions? options = null)
    {
        options ??= new CheckoutOptions();
        if (booking.Cancelled == true || booking.Refunded == true)
            throw new BusinessRuleException("This booking is cancelled or refunded, so there's nothing to pay.");
        var owed = BookingStatuses.Outstanding(booking);
        if (owed <= 0)
            throw new BusinessRuleException("This booking has already been paid. Reload the page.");
        if (string.IsNullOrWhiteSpace(PlaceholderEmails.Visible(booking.GuestEmail)))
            throw new BusinessRuleException("The guest doesn't have an email address. Add one to the guest first.");
        if (!payments.IsConfigured)
            throw new BusinessRuleException("Stripe isn't set up in this environment, so card payment links can't be made here.");

        // Legacy's lines, in the booking's order. (Legacy also charged cancelled tours; they're left out.)
        var tours = booking.Items.Where(i => i.TourId != null && i.DeletedOn == null && i.Cancelled != true).ToList();
        var tourIds = tours.Select(i => i.TourId!.Value).Distinct().ToList();
        var tourInfo = await db.Tours.AsNoTracking().Where(t => tourIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => new { t.Name, t.ImagePath }, cancellationToken);
        var lines = tours.Select(i =>
        {
            var tour = tourInfo.GetValueOrDefault(i.TourId!.Value);
            var name = tour?.Name ?? "";
            var guests = BookingStatuses.GuestCount(i);
            return new CheckoutLine(name, i.Amount * guests, 1)
            {
                Description = $"{guests} guest{(guests > 1 ? "s" : "")} at {name} on {i.StartDate.ToString("D", LegacyCulture)}",
                ImageUrl = tour?.ImagePath,
            };
        }).ToList();

        // As legacy: what's still owed goes on the first line and the others are free (a part-paid or re-priced booking).
        if (lines.Count > 0 && lines.Sum(l => l.UnitAmount) != owed)
            lines = [.. lines.Select((line, index) => line with { UnitAmount = index == 0 ? owed : 0 })];
        if (lines.Count == 0)
            lines = [new CheckoutLine($"Balance for booking {booking.InvoiceId}", owed, 1)];

        var toursLabel = string.Join(", ", lines.Select(l => l.Name));
        var session = await payments.CreateCheckoutAsync(new CheckoutRequest(booking.Id, $"{toursLabel} - Booking: {booking.InvoiceId}", lines, booking.GuestEmail)
        {
            Metadata = new Dictionary<string, string> { ["Properties"] = "", ["Tours"] = toursLabel },
            WebsiteCheckout = options.WebsiteCheckout,
            TestMode = options.TestMode,
            // As legacy: test bookings skip the customer lookup.
            FindExistingCustomer = !options.TestMode,
            SuccessUrl = string.IsNullOrWhiteSpace(options.SuccessUrl) ? null : options.SuccessUrl,
            CancelUrl = string.IsNullOrWhiteSpace(options.CancelUrl) ? null : options.CancelUrl,
        }, cancellationToken);

        if (options.RecordSession)
        {
            // As legacy's FlagBookingStripeStatus: a balance's checkout is the booking's "update" checkout.
            if (booking.AmountOutstanding is > 0)
                booking.Update(updateStripeSessionId: session.Id, additionalNotes: booking.AdditionalNotes, bookingSource: booking.BookingSource);
            else
                booking.SetStripeSessonId(session.Id);
            await db.SaveChangesAsync(cancellationToken);
        }
        return (session, owed);
    }
}

internal sealed class CreatePaymentLinkHandler(IAppDbContext db, SupplierScope scope, BookingCheckouts checkouts, IQrCodes qr) : ICommandHandler<CreatePaymentLink, PaymentLink>
{
    public async ValueTask<PaymentLink> Handle(CreatePaymentLink command, CancellationToken cancellationToken)
    {
        var booking = await BookingLoader.LoadAsync(db, scope, command.BookingId, cancellationToken);
        var (session, amount) = await checkouts.CreateAsync(booking, cancellationToken, new CheckoutOptions(RecordSession: true));
        return new PaymentLink(session.Url, qr.PngDataUrl(session.Url), amount);
    }
}

internal sealed class SendPaymentEmailHandler(IAppDbContext db, SupplierScope scope, BookingCheckouts checkouts, IBookingEmails emails, IBackgroundJobs jobs)
    : ICommandHandler<SendPaymentEmail, string>
{
    public async ValueTask<string> Handle(SendPaymentEmail command, CancellationToken cancellationToken)
    {
        var booking = await BookingLoader.LoadAsync(db, scope, command.BookingId, cancellationToken);
        var (session, _) = await checkouts.CreateAsync(booking, cancellationToken, new CheckoutOptions(RecordSession: true));
        var email = await BookingEmailLoader.LoadAsync(db, booking.Id, cancellationToken);
        jobs.EnqueueEmail(await emails.PaymentReminderAsync(email, booking.GuestEmail!, session.Url));
        return $"Payment email sent to {booking.GuestEmail}.";
    }
}

internal sealed class ConfirmCheckoutHandler(
    IAppDbContext db,
    IPaymentGateway payments,
    IBookingEmails emails,
    IBackgroundJobs jobs,
    PaidBookingNotifications notifications,
    IBusinessClock clock,
    IOptions<Site.SiteOptions> site,
    ILogger<ConfirmCheckoutHandler> logger) : ICommandHandler<ConfirmCheckout, CheckoutConfirmation>
{
    private static readonly CheckoutConfirmation Failed = new() { Message = "We couldn't confirm this payment. Please contact us." };

    public async ValueTask<CheckoutConfirmation> Handle(ConfirmCheckout command, CancellationToken cancellationToken)
    {
        if (!payments.IsConfigured)
            return Failed;
        var checkout = await payments.GetCheckoutAsync(command.SessionId, cancellationToken);
        if (checkout is not { Paid: true })
            return Failed with { Message = "This payment hasn't been completed." };

        if (!DefaultIdType.TryParse(checkout.BookingId, out var bookingId))
        {
            // Not one of ours (another checkout on the same Stripe account): as legacy, left alone (never refunded).
            logger.LogWarning("Checkout {Session} was paid but isn't for a booking (client reference {Reference}): ignored", command.SessionId, checkout.BookingId);
            return Failed;
        }

        // The return page (prerendered, then again in the browser), the webhook and the sweep can all confirm the same checkout
        // at the same moment: one at a time per booking, so a payment is recorded and confirmed once.
        await using var bookingLock = await db.LockAsync($"booking-payment:{bookingId}", TimeSpan.FromSeconds(30), cancellationToken);

        var booking = await db.Bookings.AsSplitQuery().Include(b => b.Items).ThenInclude(i => i.Guests).Include(b => b.Payments)
            .FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
        if (booking is null)
        {
            // As legacy's webhook and payment-link page: not refunded automatically. Staff refund it in Stripe if needed.
            logger.LogError("Checkout {Session} paid {Amount} for booking {Booking}, which no longer exists: not recorded or refunded", command.SessionId,
                checkout.AmountTotal, checkout.BookingId);
            return Failed with { Message = "We couldn't find the booking for this payment. Please contact us." };
        }
        if (booking.Cancelled == true || booking.Refunded == true)
            return Failed with { Message = $"Booking {booking.InvoiceId} has been cancelled. Please contact us about your payment." };

        var confirmed = new CheckoutConfirmation { Confirmed = true, Reference = booking.InvoiceId, BookingDate = booking.BookingDate, Message = "Payment received." };
        if (booking.Payments.Any(p => p.StripeSessionId == command.SessionId))
            return confirmed; // already recorded (the return page and the webhook both call this)

        // As legacy: the website's own checkout (this site's, or legacy's embedded one) copies the team on the confirmation.
        var websiteCheckout = booking.BookingSource == "Website" && booking.StripeSessionId == command.SessionId && checkout.WebsiteCheckout;
        var wasPaid = booking.IsPaid;
        // As legacy: the payment is in the booking's guest's name, not the card holder's.
        var payment = new BookingPayment(booking.Id, checkout.AmountTotal, command.SessionId, false,
            booking.GuestName, booking.GuestEmail, booking.CurrencyCode, BookingPaymentMethod.Stripe)
        {
            PaymentDate = clock.Now,
        };
        booking.Payments.Add(payment);
        db.BookingPayments.Add(payment);
        // The first card payment is the booking's checkout; a later one (for changes) its "update" checkout (as legacy).
        // (The session may already be on the booking: payment links record the checkout they send, as legacy.)
        if (string.IsNullOrEmpty(booking.StripeSessionId) || booking.StripeSessionId == command.SessionId)
            booking.SetStripeSessonId(command.SessionId);
        else
            booking.Update(updateStripeSessionId: command.SessionId, additionalNotes: booking.AdditionalNotes, bookingSource: booking.BookingSource);
        booking.ApplyManualPayment(checkout.AmountTotal);
        await db.SaveChangesAsync(cancellationToken);

        await SendConfirmationsAsync(booking, websiteCheckout, cancellationToken);
        if (!wasPaid && booking.IsPaid)
            await notifications.EnqueueAsync(booking, cancellationToken);
        return confirmed;
    }

    /// <summary>Once per booking: the guest's confirmation, and each supplier's list of who's on their tours.</summary>
    private async Task SendConfirmationsAsync(Booking booking, bool websiteCheckout, CancellationToken cancellationToken)
    {
        if (booking.ConfirmationEmailSent == true)
            return;
        var email = await BookingEmailLoader.LoadAsync(db, booking.Id, cancellationToken);
        // As legacy: to the booking's guest (not whoever paid), and the website's copied to the team (MailSettings:ToAddress).
        if (!string.IsNullOrWhiteSpace(PlaceholderEmails.Visible(booking.GuestEmail)))
        {
            // As legacy: the website's confirmation (from its basket) shows each line's total, the others the per-guest price.
            var confirmation = await emails.ConfirmationAsync(email with { LineTotals = websiteCheckout }, booking.GuestEmail);
            var team = site.Value.ContactEmail;
            jobs.EnqueueEmail(websiteCheckout && !string.IsNullOrWhiteSpace(team) ? confirmation with { Bcc = [.. confirmation.Bcc, team] } : confirmation);
        }

        var supplierIds = email.Items.Select(i => i.SupplierId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var suppliers = await db.Users.AsNoTracking().Where(u => supplierIds.Contains(u.Id) && u.Email != null)
            .Select(u => new { u.Id, u.Email }).ToListAsync(cancellationToken);
        foreach (var supplier in suppliers)
            jobs.EnqueueEmail(await emails.SupplierConfirmationAsync(email, supplier.Id, supplier.Email!));

        booking.FlagConfirmationEmailSent();
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>When a booking becomes paid, the partner webhook hears about everyone on it (if one is configured).</summary>
internal sealed class PaidBookingNotifications(IAppDbContext db, IPaidBookingWebhook webhook, IBackgroundJobs jobs)
{
    public async Task EnqueueAsync(Booking booking, CancellationToken cancellationToken)
    {
        if (!webhook.IsEnabled)
            return;
        var userIds = booking.Items.SelectMany(i => i.Guests ?? []).Where(g => g.DeletedOn == null).Select(g => g.GuestId.ToString())
            .Append(booking.GuestId ?? "").Where(id => id != "").Distinct().ToList();
        var cloudbedsIds = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id) && u.CloudbedsGuestId != null && u.CloudbedsGuestId != "")
            .Select(u => u.CloudbedsGuestId!).Distinct().ToListAsync(cancellationToken);
        if (cloudbedsIds.Count == 0)
            return;
        var bookingId = booking.Id;
        jobs.Enqueue<IPaidBookingWebhook>(webhook => webhook.NotifyAsync(bookingId, cloudbedsIds, CancellationToken.None));
    }
}
