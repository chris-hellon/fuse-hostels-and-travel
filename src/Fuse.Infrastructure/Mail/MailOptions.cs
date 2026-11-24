namespace Fuse.Infrastructure.Mail;

/// <summary>The "Mail" configuration section. Server credentials belong in the appsettings files (not in git), never in source.</summary>
public sealed class MailOptions
{
    public const string Section = "Mail";

    /// <summary>"Pickup" (default): write .eml files to <see cref="PickupDirectory"/>. "Smtp": send through the server below.</summary>
    public string Mode { get; set; } = "Pickup";

    /// <summary>Relative to the app's content root.</summary>
    public string PickupDirectory { get; set; } = ".mail";

    public string? Host { get; set; }
    public int Port { get; set; } = 465;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "admin@travaloud.com";
    public string DisplayName { get; set; } = "Fuse Hostels & Travel";

    /// <summary>Copied on every email (legacy BccAddress).</summary>
    public List<string> Bcc { get; set; } = [];

    /// <summary>
    /// Outside production, SMTP only delivers to these addresses (anyone else is left out and logged), so
    /// testing can never email a real guest or supplier.
    /// </summary>
    public List<string> AllowedRecipients { get; set; } = [];

    /// <summary>
    /// TLS certificate revocation checks (OCSP/CRL); legacy MailSettings:CheckCertificateRevocation. False skips only the
    /// revocation lookup (it can't complete on some machines): the certificate chain is still checked.
    /// </summary>
    public bool CheckCertificateRevocation { get; set; } = true;

    /// <summary>Gmail OAuth2 sign-in (legacy GmailOAuth); when configured it replaces <see cref="UserName"/>/<see cref="Password"/>.</summary>
    public GmailOAuthOptions GmailOAuth { get; set; } = new();

    public bool UsesSmtp => string.Equals(Mode, "Smtp", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The "Mail:GmailOAuth" section (legacy GmailOAuth): signs in to Gmail SMTP with XOAUTH2, an access token minted from
/// <see cref="RefreshToken"/>, so mail goes out as a Workspace mailbox (contact@) that can't use an App Password.
/// </summary>
public sealed class GmailOAuthOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RefreshToken { get; set; }

    /// <summary>The Google account the token belongs to: the SMTP login.</summary>
    public string? AccountEmail { get; set; }

    /// <summary>The From address: the account itself (default) or one of its verified "send mail as" aliases.</summary>
    public string? FromAddress { get; set; }

    public string? FromName { get; set; }

    /// <summary>Everything needed to sign in is set; otherwise the user name and password are used, as before.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(RefreshToken) && !string.IsNullOrWhiteSpace(AccountEmail);
}

/// <summary>The "Mail:Brand" section: how emails look (legacy MailSettings colours and logo).</summary>
public sealed class MailBrand
{
    public string TenantName { get; set; } = "Fuse Hostels & Travel";
    public string PrimaryBackgroundColor { get; set; } = "#090511";
    public string HeaderBackgroundColor { get; set; } = "#ab3dff";
    public string LogoImageUrl { get; set; } = "https://travaloud.azureedge.net/fuse/assets/images/Fuse-primary-logo.svg";
    public string ContactUrl { get; set; } = "https://fusehostelsandtravel.com/contact";
}
