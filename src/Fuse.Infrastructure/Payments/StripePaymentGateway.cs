using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using Fuse.Application.Common.Payments;
using Fuse.Contracts.Common;

namespace Fuse.Infrastructure.Payments;

/// <summary>Stripe with the configured (test, outside production) key; refuses everything when none is set.</summary>
internal sealed class StripePaymentGateway(IOptions<StripeOptions> options) : IPaymentGateway
{
    private StripeClient? _client, _testClient;

    /// <summary>Where legacy's website checkout returned (payment links return to /travaloud-payment-confirmation).</summary>
    private const string WebsiteReturnPath = "/payment-confirmation/";

    public bool IsConfigured => options.Value.IsConfigured;

    private StripeClient Client => _client ??= IsConfigured
        ? new StripeClient(options.Value.SecretKey)
        : throw new BusinessRuleException("Stripe isn't set up in this environment.");

    /// <summary>Stripe's test mode (test bookings made through the public API).</summary>
    private StripeClient TestClient => _testClient ??= options.Value.TestModeKey is { } key
        ? new StripeClient(key)
        : throw new BusinessRuleException("Stripe test mode isn't set up (Stripe:TestSecretKey).");

    /// <summary>A checkout session belongs to the account (live or test) that made it.</summary>
    private StripeClient For(string checkoutSessionId) =>
        checkoutSessionId.StartsWith("cs_test_", StringComparison.Ordinal) && !StripeOptions.IsTestKey(options.Value.SecretKey) ? TestClient : Client;

    public async Task RefundAsync(string checkoutSessionId, decimal amount, CancellationToken cancellationToken)
    {
        var session = await new SessionService(For(checkoutSessionId)).GetAsync(checkoutSessionId, cancellationToken: cancellationToken);
        if (string.IsNullOrEmpty(session.PaymentIntentId))
            throw new BusinessRuleException("Stripe has no payment for this booking's checkout, so there's nothing to refund.");

        try
        {
            await new RefundService(For(checkoutSessionId)).CreateAsync(new RefundCreateOptions
            {
                PaymentIntent = session.PaymentIntentId,
                Amount = ToCents(amount),
            }, cancellationToken: cancellationToken);
        }
        catch (StripeException failure)
        {
            throw new BusinessRuleException($"Stripe refused the refund: {failure.StripeError?.Message ?? failure.Message}");
        }
    }

    public async Task<decimal> SessionTotalAsync(string checkoutSessionId, CancellationToken cancellationToken)
    {
        var session = await new SessionService(For(checkoutSessionId)).GetAsync(checkoutSessionId, cancellationToken: cancellationToken);
        return session.PaymentStatus == "paid" ? (session.AmountTotal ?? 0) / 100m : 0m;
    }

    public async Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        var site = options.Value.SiteUrl?.TrimEnd('/')
                   ?? throw new BusinessRuleException("Stripe:SiteUrl isn't set, so checkout has nowhere to return to.");
        var create = new SessionCreateOptions
        {
            ClientReferenceId = request.BookingId.ToString(),
            Mode = "payment",
            LineItems =
            [
                .. request.Lines.Select(line => new SessionLineItemOptions
                {
                    Quantity = line.Quantity,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "usd",
                        UnitAmount = ToCents(line.UnitAmount),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = line.Name,
                            Description = line.Description,
                            Images = line.ImageUrl is { } image && image.Contains("http", StringComparison.Ordinal) ? [image] : null,
                        },
                    },
                }),
            ],
            PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                ReceiptEmail = request.CustomerEmail,
                Description = request.Description,
                Metadata = new Dictionary<string, string>(request.Metadata),
            },
            // As legacy: payable for Stripe's default 24 hours. The website's checkout returns where legacy's did.
            SuccessUrl = request.SuccessUrl ?? site + (request.WebsiteCheckout ? WebsiteReturnPath : "/travaloud-payment-confirmation/") + "{CHECKOUT_SESSION_ID}",
            CancelUrl = request.CancelUrl ?? site + "/order-failed",
        };

        var client = request.TestMode ? TestClient : Client;
        try
        {
            // As legacy: the guest's existing Stripe customer when they have one, else a new customer from their email.
            var customerId = request.FindExistingCustomer && !string.IsNullOrWhiteSpace(request.CustomerEmail)
                ? (await new CustomerService(client).SearchAsync(new CustomerSearchOptions { Query = $"email:'{request.CustomerEmail}'" },
                    cancellationToken: cancellationToken)).Data.FirstOrDefault()?.Id
                : null;
            if (customerId is not null)
                create.Customer = customerId;
            else
            {
                create.CustomerEmail = request.CustomerEmail;
                create.CustomerCreation = "always";
            }

            var session = await new SessionService(client).CreateAsync(create, cancellationToken: cancellationToken);
            return new CheckoutSession(session.Id, session.Url);
        }
        catch (StripeException failure)
        {
            throw new BusinessRuleException($"Stripe couldn't create the checkout: {failure.StripeError?.Message ?? failure.Message}");
        }
    }

    public async Task<CheckoutResult?> GetCheckoutAsync(string checkoutSessionId, CancellationToken cancellationToken)
    {
        try
        {
            var session = await new SessionService(For(checkoutSessionId)).GetAsync(checkoutSessionId, cancellationToken: cancellationToken);
            return new CheckoutResult(session.Id, session.Status == "complete" && session.PaymentStatus == "paid", session.ClientReferenceId,
                (session.AmountTotal ?? 0) / 100m, session.CustomerDetails?.Email, session.CustomerDetails?.Name)
            {
                // Legacy's website checkout was embedded; this site's returns to legacy's website page.
                WebsiteCheckout = session.UiMode == "embedded"
                                  || (options.Value.SiteUrl is { } site && session.SuccessUrl is { } success
                                      && success.StartsWith(site.TrimEnd('/') + WebsiteReturnPath, StringComparison.OrdinalIgnoreCase)),
            };
        }
        catch (StripeException failure) when (failure.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public CheckoutEvent? ReadCheckoutEvent(string payload, string signature)
    {
        var secret = options.Value.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Stripe:WebhookSecret isn't set.");
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signature, secret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException) when (!string.IsNullOrWhiteSpace(options.Value.TestWebhookSecret))
        {
            // Test-mode events (the public API's test bookings) are signed with the test endpoint's secret.
            stripeEvent = EventUtility.ConstructEvent(payload, signature, options.Value.TestWebhookSecret, throwOnApiVersionMismatch: false);
        }
        if (stripeEvent.Data.Object is not Session session)
            return null;
        return stripeEvent.Type switch
        {
            EventTypes.CheckoutSessionCompleted or EventTypes.CheckoutSessionAsyncPaymentSucceeded => new CheckoutEvent(session.Id, Completed: true),
            EventTypes.CheckoutSessionExpired => new CheckoutEvent(session.Id, Completed: false),
            _ => null,
        };
    }

    private static long ToCents(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
