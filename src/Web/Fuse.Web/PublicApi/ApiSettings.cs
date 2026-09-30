namespace Fuse.Web.PublicApi;

/// <summary>The public booking API's configuration: the "ApiSettings" section, as legacy (Configurations/api.json).</summary>
public sealed class ApiSettings
{
    public const string Section = "ApiSettings";

    public JwtSettings Jwt { get; set; } = new();

    /// <summary>
    /// The applications allowed to use the API. A caller presents a ClientId/ClientSecret for a token, which then acts
    /// as that client's service ("virtual") staff user. Each client's WebhookUrl is read by the paid-booking webhook.
    /// </summary>
    public List<ApiClient> Clients { get; set; } = [];
}

public sealed class JwtSettings
{
    /// <summary>Symmetric signing key (at least 32 characters). Keep secret.</summary>
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "Travaloud.Admin";
    public string Audience { get; set; } = "Travaloud.Api";
    public int TokenLifetimeMinutes { get; set; } = 60;
}

public sealed class ApiClient
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    /// <summary>The service staff user this client acts as (bookings it makes are credited to them).</summary>
    public string UserId { get; set; } = "";

    /// <summary>Test bookings (isDevelopment) are credited to this user instead, when set.</summary>
    public string? DevUserId { get; set; }

    public string? WebhookUrl { get; set; }
}
