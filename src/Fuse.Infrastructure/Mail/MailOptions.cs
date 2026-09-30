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

    public bool UsesSmtp => string.Equals(Mode, "Smtp", StringComparison.OrdinalIgnoreCase);
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
