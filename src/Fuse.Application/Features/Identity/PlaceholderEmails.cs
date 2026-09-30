namespace Fuse.Application.Features.Identity;

/// <summary>
/// Accounts must have a unique email, but guests added at the desk or at checkout may not give one; as legacy, those
/// get a made-up address at this domain. It's never shown or emailed: everything that reads an email uses
/// <see cref="Visible"/>.
/// </summary>
internal static class PlaceholderEmails
{
    public const string Domain = "travaloudguest.com";

    /// <summary>For EF queries: ends with this when it's a placeholder.</summary>
    public const string Suffix = "@" + Domain;

    public static string New() => $"{Guid.NewGuid():N}{Suffix}";

    public static bool Is(string? email) => email?.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The email, or null when it's a placeholder.</summary>
    public static string? Visible(string? email) => Is(email) ? null : email;
}
