namespace Fuse.Contracts.Api;

/// <summary>
/// A public form that sends email (contact, enquiries): the host limits how often one address can send it, so a bot
/// can't flood the inbox even past the captcha.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RateLimitedAttribute : Attribute
{
    public const string Policy = "forms";

    /// <summary>Sends allowed per address per 10 minutes, unless RateLimits:FormsPer10Minutes says otherwise.</summary>
    public const int DefaultPer10Minutes = 20;
}
