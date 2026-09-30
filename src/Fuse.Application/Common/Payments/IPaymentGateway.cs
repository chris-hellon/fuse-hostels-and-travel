namespace Fuse.Application.Common.Payments;

/// <summary>
/// Card payments (Stripe). Only test keys are ever used outside production; with no keys configured
/// every call refuses with a clear message instead of reaching Stripe.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>False when no Stripe keys are configured for this environment.</summary>
    bool IsConfigured { get; }

    /// <summary>Refunds <paramref name="amount"/> of the payment taken by a checkout session.</summary>
    Task RefundAsync(string checkoutSessionId, decimal amount, CancellationToken cancellationToken);

    /// <summary>The total charged by a checkout session (0 when it wasn't paid).</summary>
    Task<decimal> SessionTotalAsync(string checkoutSessionId, CancellationToken cancellationToken);

    /// <summary>A hosted checkout page for a booking (returns to the site's payment confirmation page).</summary>
    Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>A checkout session as Stripe has it now; null when Stripe doesn't know it.</summary>
    Task<CheckoutResult?> GetCheckoutAsync(string checkoutSessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks a webhook's signature and returns the checkout it's about when it's one we act on (completed or
    /// expired; null for other events). Throws when the signature is wrong or webhooks aren't configured.
    /// </summary>
    CheckoutEvent? ReadCheckoutEvent(string payload, string signature);
}

public sealed record CheckoutLine(string Name, decimal UnitAmount, int Quantity)
{
    /// <summary>Shown under the name on Stripe's page ("2 guests at … on Saturday, September 26, 2026").</summary>
    public string? Description { get; init; }
    /// <summary>The tour's photo on Stripe's page (http(s) URLs only).</summary>
    public string? ImageUrl { get; init; }
}

public sealed record CheckoutRequest(DefaultIdType BookingId, string Description, IReadOnlyList<CheckoutLine> Lines, string? CustomerEmail)
{
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    /// <summary>
    /// The website's own checkout: returns to legacy's website confirmation page (/payment-confirmation), which is how it's
    /// told apart from a payment link when paid (<see cref="CheckoutResult.WebsiteCheckout"/>).
    /// </summary>
    public bool WebsiteCheckout { get; init; }
    /// <summary>Stripe's test mode whatever the environment (the public API's test bookings); needs Stripe:TestSecretKey.</summary>
    public bool TestMode { get; init; }
    /// <summary>
    /// As legacy: pay as the guest's existing Stripe customer (found by <see cref="CustomerEmail"/>) rather than creating a
    /// new one each time. Legacy's test bookings skipped the lookup (and always created one).
    /// </summary>
    public bool FindExistingCustomer { get; init; } = true;
    /// <summary>Where Stripe returns after paying / cancelling, instead of the site's confirmation and order-failed pages.</summary>
    public string? SuccessUrl { get; init; }
    public string? CancelUrl { get; init; }
}

/// <param name="Completed">Paid (or payment on its way); false when the session expired unpaid.</param>
public sealed record CheckoutEvent(string SessionId, bool Completed);

public sealed record CheckoutSession(string Id, string Url);

/// <param name="BookingId">The booking the session was created for (Stripe's client reference).</param>
public sealed record CheckoutResult(string Id, bool Paid, string? BookingId, decimal AmountTotal, string? CustomerEmail, string? CustomerName)
{
    /// <summary>
    /// The website's own checkout (<see cref="CheckoutRequest.WebsiteCheckout"/>, or legacy's embedded one), not a payment
    /// link or the public API's.
    /// </summary>
    public bool WebsiteCheckout { get; init; }
}
