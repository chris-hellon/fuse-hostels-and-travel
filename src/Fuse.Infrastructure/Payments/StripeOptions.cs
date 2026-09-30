namespace Fuse.Infrastructure.Payments;

/// <summary>The "Stripe" configuration section. Keys belong in the appsettings files (not in git), never in source.</summary>
public sealed class StripeOptions
{
    public const string Section = "Stripe";

    public string? SecretKey { get; set; }
    public string? PublishableKey { get; set; }

    /// <summary>The public site checkout returns to (success / cancel pages live there).</summary>
    public string? SiteUrl { get; set; }

    /// <summary>Signs Stripe's calls to /api/stripe/webhook; without it the webhook endpoint is off.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// Stripe test-mode keys for the public API's test bookings (isDevelopment), which use test mode even in production.
    /// Outside production the main keys are test keys already and are used when these aren't set.
    /// </summary>
    public string? TestSecretKey { get; set; }
    public string? TestWebhookSecret { get; set; }

    /// <summary>Live keys are refused unless this is set (only in production's own settings).</summary>
    public bool AllowLiveKeys { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);

    /// <summary>Like the database guard: nothing outside production may take or refund real money.</summary>
    public void EnsureSafe()
    {
        if (!IsConfigured || AllowLiveKeys)
            return;
        if (!IsTestKey(SecretKey))
            throw new InvalidOperationException(
                "Refusing to start with a live (or unrecognised) Stripe key: only test keys (sk_test_...) are allowed here. " +
                "Set Stripe:AllowLiveKeys=true in production's settings to use live keys (deliberately).");
    }

    public static bool IsTestKey(string? key) =>
        key is not null && (key.StartsWith("sk_test_", StringComparison.Ordinal) || key.StartsWith("rk_test_", StringComparison.Ordinal));

    /// <summary>
    /// The key for test mode: <see cref="TestSecretKey"/>, else the main key. (Legacy's "Development" Stripe settings for
    /// Fuse were the same live keys as production, so its API test bookings used them too.)
    /// </summary>
    public string? TestModeKey => !string.IsNullOrWhiteSpace(TestSecretKey) ? TestSecretKey : SecretKey;
}
