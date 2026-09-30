namespace Fuse.Application.Common.Security;

/// <summary>
/// The "ReCaptcha" configuration section (Google reCAPTCHA v2, the "I'm not a robot" box, as legacy). The site key is
/// public (the page needs it); the secret belongs in the appsettings files (not in git), never in source. Development uses
/// Google's test keys, which always pass.
/// </summary>
public sealed class CaptchaOptions
{
    public const string Section = "ReCaptcha";

    public string SiteKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
}

/// <summary>Checks a reCAPTCHA response from the browser with Google.</summary>
public interface ICaptcha
{
    Task<bool> VerifyAsync(string response, CancellationToken cancellationToken);
}
