using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Fuse.Application.Common.Payments;

namespace Fuse.Infrastructure.Payments;

/// <summary>The public API's clients in the "ApiSettings" section (as legacy), for their paid-booking webhooks.</summary>
public sealed class BookingWebhookOptions
{
    public const string Section = "ApiSettings";

    public List<BookingWebhookClient> Clients { get; set; } = [];

    /// <summary>Partner webhooks are production-only unless this is set (deliberately, e.g. against a test endpoint).</summary>
    public bool AllowOutsideProduction { get; set; }
}

public sealed class BookingWebhookClient
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? WebhookUrl { get; set; }
}

/// <summary>POSTs the Cloudbeds guest ids to each client's URL with HTTP Basic auth; a failure throws so the job retries.</summary>
internal sealed class PaidBookingWebhook(HttpClient http, IOptions<BookingWebhookOptions> options, IHostEnvironment environment) : IPaidBookingWebhook
{
    private IEnumerable<BookingWebhookClient> Targets => options.Value.Clients.Where(c => !string.IsNullOrWhiteSpace(c.WebhookUrl));

    public bool IsEnabled => Targets.Any() && (environment.IsProduction() || options.Value.AllowOutsideProduction);

    public async Task NotifyAsync(DefaultIdType bookingId, IReadOnlyList<string> cloudbedsGuestIds, CancellationToken cancellationToken)
    {
        if (!IsEnabled || cloudbedsGuestIds.Count == 0)
            return;
        foreach (var client in Targets)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, client.WebhookUrl) { Content = JsonContent.Create(new { cloudbedsGuestIds }) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{client.ClientId}:{client.ClientSecret}")));
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }
}
