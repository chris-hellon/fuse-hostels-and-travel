using System.Globalization;
using System.Text.RegularExpressions;
using Fuse.Application.Common.Cloudbeds;
using Fuse.Contracts.Identity;
using Fuse.Shared.Globalization;

namespace Fuse.Application.Features.Identity;

internal sealed partial class GetGuestCloudbedsProfileHandler(UserKinds kinds, IAppDbContext db, ICloudbedsClient cloudbeds,
    ILogger<GetGuestCloudbedsProfileHandler> logger) : IQueryHandler<GetGuestCloudbedsProfile, CloudbedsGuestLookup>
{
    public async ValueTask<CloudbedsGuestLookup> Handle(GetGuestCloudbedsProfile query, CancellationToken cancellationToken)
    {
        var guest = await kinds.GetDetailsAsync(query.Id, staff: false, cancellationToken);
        if (string.IsNullOrWhiteSpace(guest.CloudbedsGuestId))
            return new(null, null);

        // A Cloudbeds guest id only means something inside the property it belongs to, so ask each
        // connected property in turn (as legacy did) and take the first that knows the guest.
        var properties = await db.Properties.AsNoTracking()
            .Where(p => p.CloudbedsPropertyId != null && p.CloudbedsApiKey != null)
            .Select(p => new { p.CloudbedsPropertyId, p.CloudbedsApiKey })
            .ToListAsync(cancellationToken);

        foreach (var property in properties)
        {
            try
            {
                var found = await cloudbeds.GetGuestAsync(property.CloudbedsPropertyId!, property.CloudbedsApiKey!, guest.CloudbedsGuestId, cancellationToken);
                if (found is not null)
                    return new(guest.CloudbedsGuestId, ToProfile(found));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Not this property's guest (Cloudbeds answers 4xx), or Cloudbeds is down: try the next.
                logger.LogDebug(ex, "Cloudbeds property {PropertyId} didn't return guest {GuestId}", property.CloudbedsPropertyId, guest.CloudbedsGuestId);
            }
        }

        return new(guest.CloudbedsGuestId, null);
    }

    /// <summary>Mirrors the legacy guest import/sync mapping, so the values offered are the ones a sync would write.</summary>
    internal static UserProfile ToProfile(CloudbedsGuest guest)
    {
        var profile = new UserProfile
        {
            FirstName = CleanName(guest.FirstName?.TrimStart()) ?? "",
            LastName = CleanName(guest.LastName) ?? "",
            PhoneNumber = Blank(guest.Phone),
            DateOfBirth = ParseDate(guest.BirthDate),
            Gender = NormaliseGender(guest.Gender),
            Nationality = CountryName(guest.Country),
        };

        if (guest.DocumentType == "Passport" && !string.IsNullOrEmpty(guest.DocumentNumber))
        {
            profile.PassportNumber = guest.DocumentNumber;
            profile.PassportIssueDate = ParseDate(guest.DocumentIssueDate);
            profile.PassportExpiryDate = ParseDate(guest.DocumentExpirationDate);
            profile.PassportIssuingCountry = CountryName(guest.DocumentIssuingCountry);
        }
        else
        {
            // Older bookings kept the passport number in a custom field.
            profile.PassportNumber = Blank(guest.CustomFields.FirstOrDefault(f => PassportFields.Contains(f.Name ?? ""))?.Value);
        }

        return profile;
    }

    private static readonly HashSet<string> PassportFields = ["Passport Number", "pp", "Passport number", "passport number"];

    /// <summary>Cloudbeds names can carry notes like "Anna [2 beds] (late)"; the import strips them.</summary>
    private static string? CleanName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : Notes().Replace(name, "").Trim();

    [GeneratedRegex(@"\[[^\]]*\]|\([^()]*\)")]
    private static partial Regex Notes();

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? CountryName(string? value) => Blank(value) is { } v ? Countries.DisplayName(v) : null;

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year > 1 ? date.Date : null;

    private static string? NormaliseGender(string? gender) => gender?.Trim().ToUpperInvariant() switch
    {
        null or "" => null,
        "M" or "MALE" => "Male",
        "F" or "FEMALE" => "Female",
        _ => gender.Trim(),
    };
}
