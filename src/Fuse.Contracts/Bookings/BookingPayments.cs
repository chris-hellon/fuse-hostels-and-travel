namespace Fuse.Contracts.Bookings;

/// <summary>A Stripe checkout for what a booking owes: the link, and a QR code of it (PNG data URL).</summary>
public sealed record PaymentLink(string Url, string QrCodeDataUrl, decimal Amount);

/// <summary>
/// A card payment link for what the guest owes (legacy "Generate Payment QR"). Paying it marks the
/// booking paid (see <see cref="ConfirmCheckout"/>).
/// </summary>
[Post("bookings/{BookingId}/payment-link"), Permission(FuseAction.Update, FuseResource.Bookings)]
public sealed record CreatePaymentLink(DefaultIdType BookingId) : ICommand<PaymentLink>;

/// <summary>Emails the guest a payment reminder with a card payment link (legacy "Send Payment Email").</summary>
[Post("bookings/{BookingId}/payment-email"), Permission(FuseAction.Update, FuseResource.Bookings)]
public sealed record SendPaymentEmail(DefaultIdType BookingId) : ICommand<string>;

public sealed record CheckoutConfirmation
{
    public bool Confirmed { get; init; }
    public int? Reference { get; init; }
    public DateTime? BookingDate { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>
/// Records a completed Stripe checkout: the payment, the booking paid, the confirmation emails. Called by
/// the site's payment confirmation page and by Stripe's webhook; running it twice changes nothing.
/// Anyone may call it: the session id comes from Stripe and is checked with Stripe.
/// </summary>
[Post("payments/checkout/{SessionId}/confirm"), Public]
public sealed record ConfirmCheckout(string SessionId) : ICommand<CheckoutConfirmation>;

public sealed class ConfirmCheckoutValidator : AbstractValidator<ConfirmCheckout>
{
    public ConfirmCheckoutValidator() => RuleFor(x => x.SessionId).NotEmpty().MaximumLength(255).Matches("^cs_[A-Za-z0-9_]+$");
}
