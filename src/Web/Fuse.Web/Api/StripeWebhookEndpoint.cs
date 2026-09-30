using Mediator;
using Microsoft.Extensions.Options;
using Fuse.Application.Common.Payments;
using Fuse.Contracts.Bookings;
using Fuse.Infrastructure.Payments;

namespace Fuse.Web.Api;

/// <summary>
/// Stripe calls this when a checkout completes, so a booking is marked paid even if the guest never
/// reaches the confirmation page. An expired checkout is only logged: as legacy, its booking stays Unpaid for staff.
/// Off (404) until Stripe:WebhookSecret is set; the signature is checked.
/// </summary>
public static class StripeWebhookEndpoint
{
    public const string Route = "/api/stripe/webhook";

    /// <summary>Legacy's webhook address, which Stripe's dashboard points at: served too.</summary>
    public const string LegacyRoute = "/webhooks/stripe-payment";

    public static void MapStripeWebhook(this IEndpointRouteBuilder app)
    {
        foreach (var route in (string[])[Route, LegacyRoute])
            Map(app, route);
    }

    private static void Map(IEndpointRouteBuilder app, string route) =>
        app.MapPost(route, async (HttpRequest request, IPaymentGateway payments, IOptions<StripeOptions> options, ISender sender,
                ILogger<StripeOptions> logger, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(options.Value.WebhookSecret) || !payments.IsConfigured)
                    return Results.NotFound();

                using var reader = new StreamReader(request.Body);
                var payload = await reader.ReadToEndAsync(cancellationToken);
                CheckoutEvent? checkout;
                try
                {
                    checkout = payments.ReadCheckoutEvent(payload, request.Headers["Stripe-Signature"].ToString());
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Stripe webhook rejected: bad signature or payload");
                    return Results.BadRequest();
                }

                if (checkout is { Completed: true })
                {
                    var result = await sender.Send(new ConfirmCheckout(checkout.SessionId), cancellationToken);
                    logger.LogInformation("Stripe webhook for {Session}: {Message}", checkout.SessionId, result.Message);
                }
                else if (checkout is { Completed: false })
                {
                    // As legacy, the booking stays Unpaid (holding its places) for staff to delete.
                    logger.LogInformation("Stripe webhook: checkout {Session} expired unpaid", checkout.SessionId);
                }
                return Results.Ok();
            })
            .AllowAnonymous()
            .DisableAntiforgery()
            .ExcludeFromDescription();
}
