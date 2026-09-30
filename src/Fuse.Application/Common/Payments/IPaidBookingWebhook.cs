namespace Fuse.Application.Common.Payments;

/// <summary>
/// Tells the configured partner webhooks that a booking has been paid, with the Cloudbeds guest ids of
/// everyone on it (legacy NotifyBookingPaidWebhook). Off unless a webhook URL is configured, and never
/// outside production unless explicitly allowed.
/// </summary>
public interface IPaidBookingWebhook
{
    bool IsEnabled { get; }

    Task NotifyAsync(DefaultIdType bookingId, IReadOnlyList<string> cloudbedsGuestIds, CancellationToken cancellationToken);
}

/// <summary>QR codes as PNG data URLs (for payment links).</summary>
public interface IQrCodes
{
    string PngDataUrl(string text);
}
