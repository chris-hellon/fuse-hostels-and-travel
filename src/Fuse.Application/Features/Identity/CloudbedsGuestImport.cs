using System.Data;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Fuse.Application.Common.Cloudbeds;
using Fuse.Domain.Identity;

namespace Fuse.Application.Features.Identity;

/// <summary>
/// Legacy's "Cloudbeds Guest Import" (every 30 minutes): each hostel's in-house and arriving guests from its Cloudbeds
/// account become guests here, so staff can find them to book tours. A guest is matched to an existing account as
/// legacy did (SearchCloudbedsGuests: their Cloudbeds id, else email and name). New ones are created with the email
/// Cloudbeds has, even one shared with other accounts, as legacy; matched ones get their latest Cloudbeds id (as legacy)
/// and any details they're missing. Unlike legacy, nothing else already on an account is overwritten, and the email
/// is never changed: staff edits and guests' own details win, and an email change here could move someone's sign-in.
/// </summary>
internal sealed class CloudbedsGuestImport(IAppDbContext db, ICloudbedsClient cloudbeds, UserManager<ApplicationUser> users,
    ILogger<CloudbedsGuestImport> logger)
{
    private static readonly string[] Statuses = ["in_house", "not_checked_in"];

    public sealed record Result(int Found, int Created, int Updated, int Failed);

    public async Task<Result> ImportAsync(CancellationToken cancellationToken)
    {
        var guests = await FetchAsync(cancellationToken);
        if (guests.Count == 0)
            return new(0, 0, 0, 0);

        var matches = await MatchAsync(guests, cancellationToken);
        int created = 0, updated = 0, failed = 0;
        foreach (var guest in guests)
        {
            try
            {
                var changed = matches.TryGetValue(guest.GuestId!, out var userId) && userId is not null
                    ? await UpdateAsync(userId, guest)
                    : await CreateAsync(guest);
                if (changed == Change.Created)
                    created++;
                else if (changed == Change.Updated)
                    updated++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogWarning(ex, "Cloudbeds guest {GuestId} couldn't be imported", guest.GuestId);
            }
        }

        logger.LogInformation("Cloudbeds guest import: {Found} guests, {Created} created, {Updated} updated, {Failed} failed", guests.Count, created, updated, failed);
        return new(guests.Count, created, updated, failed);
    }

    /// <summary>Every connected hostel's guests, one per Cloudbeds id. A hostel that fails is skipped, not the run.</summary>
    private async Task<List<CloudbedsGuest>> FetchAsync(CancellationToken cancellationToken)
    {
        var properties = await db.Properties.AsNoTracking()
            .Where(p => p.CloudbedsPropertyId != null && p.CloudbedsPropertyId != "" && p.CloudbedsApiKey != null && p.CloudbedsApiKey != "")
            .Select(p => new { p.Name, p.CloudbedsPropertyId, p.CloudbedsApiKey })
            .ToListAsync(cancellationToken);

        var guests = new Dictionary<string, CloudbedsGuest>();
        foreach (var property in properties)
        foreach (var status in Statuses)
        {
            try
            {
                foreach (var guest in await cloudbeds.ListGuestsByStatusAsync(property.CloudbedsPropertyId!, property.CloudbedsApiKey!, status, cancellationToken))
                    if (!string.IsNullOrWhiteSpace(guest.GuestId) && !string.IsNullOrWhiteSpace(guest.FirstName))
                        guests.TryAdd(guest.GuestId.Trim(), guest with { GuestId = guest.GuestId.Trim() });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Cloudbeds guests ({Status}) couldn't be read for {Property}", status, property.Name);
            }
        }
        return [.. guests.Values];
    }

    /// <summary>Cloudbeds id → existing account id (null when new), by legacy's tuned matching proc.</summary>
    private async Task<Dictionary<string, string?>> MatchAsync(IReadOnlyList<CloudbedsGuest> guests, CancellationToken cancellationToken)
    {
        var table = new DataTable();
        foreach (var column in new[] { "GuestId", "FirstName", "LastName", "Gender", "PhoneNumber", "Email", "Nationality" })
            table.Columns.Add(column, typeof(string));
        table.Columns.Add("DateOfBirth", typeof(DateTime));
        foreach (var guest in guests)
        {
            var profile = GetGuestCloudbedsProfileHandler.ToProfile(guest);
            table.Rows.Add(guest.GuestId, profile.FirstName, profile.LastName, profile.Gender, profile.PhoneNumber, Blank(guest.Email),
                profile.Nationality, (object?)profile.DateOfBirth ?? DBNull.Value);
        }

        var rows = await db.QueryProcedureAsync<MatchRow>("SearchCloudbedsGuests",
            new { GuestSearch = table.AsTableValuedParameter("dbo.GuestSearchWithGuestId") }, cancellationToken);
        return rows.Where(r => r.GuestId is not null).GroupBy(r => r.GuestId!).ToDictionary(g => g.Key, g => g.First().Id);
    }

    private sealed class MatchRow
    {
        public string? GuestId { get; init; }
        public string? Id { get; init; }
    }

    private enum Change { None, Created, Updated }

    private async Task<Change> CreateAsync(CloudbedsGuest guest)
    {
        var profile = GetGuestCloudbedsProfileHandler.ToProfile(guest);
        var email = Blank(guest.Email);
        profile.Email = email;
        var user = IdentityResults.NewUser(profile); // no email: a placeholder
        user.CloudbedsGuestId = guest.GuestId;
        // As legacy, the guest keeps the email Cloudbeds has even when it's shared (a couple, a group) or malformed, so
        // it's never an Identity-validated write. They sign in with it only when no one else has it and it's usable
        // as a username; otherwise their username is a placeholder (legacy's were random GUIDs).
        if (email is not null && !await UsableAsUserNameAsync(email))
            user.UserName = PlaceholderEmails.New();
        user.NormalizedUserName = users.NormalizeName(user.UserName);
        user.NormalizedEmail = users.NormalizeEmail(user.Email);
        user.SecurityStamp = Guid.NewGuid().ToString("N").ToUpperInvariant();

        _guestRoleId ??= await db.Roles.Where(r => r.NormalizedName == users.NormalizeName(FuseRoles.Guest))
                             .Select(r => r.Id).FirstOrDefaultAsync()
                         ?? throw new InvalidOperationException("The Guest role is missing.");
        var role = new IdentityUserRole<string> { UserId = user.Id, RoleId = _guestRoleId };
        db.Users.Add(user);
        db.UserRoles.Add(role);
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            // Not retried by the next guest's save.
            db.Users.Entry(user).State = EntityState.Detached;
            db.UserRoles.Entry(role).State = EntityState.Detached;
            throw;
        }
        return Change.Created;
    }

    private string? _guestRoleId;

    private async Task<bool> UsableAsUserNameAsync(string email)
    {
        var allowed = users.Options.User.AllowedUserNameCharacters;
        if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email)
            || !string.IsNullOrEmpty(allowed) && email.Any(c => !allowed.Contains(c)))
            return false;
        var normalized = users.NormalizeEmail(email);
        return !await db.Users.AnyAsync(u => u.NormalizedEmail == normalized || u.NormalizedUserName == normalized);
    }

    private async Task<Change> UpdateAsync(string userId, CloudbedsGuest guest)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null)
            return Change.None;
        var from = GetGuestCloudbedsProfileHandler.ToProfile(guest);
        var changed = false;
        void Fill<T>(T current, T value, Action<T> set)
        {
            if (IsEmpty(current) && !IsEmpty(value))
            {
                set(value);
                changed = true;
            }
        }

        // As legacy: the guest's latest Cloudbeds id always replaces the one on file (ids are per hostel account, and
        // the webhook, stays lookup and reports use the newest).
        if (user.CloudbedsGuestId != guest.GuestId)
        {
            user.CloudbedsGuestId = guest.GuestId;
            changed = true;
        }
        Fill(user.PhoneNumber, from.PhoneNumber, v => user.PhoneNumber = v);
        Fill(user.DateOfBirth, from.DateOfBirth, v => user.DateOfBirth = v);
        Fill(user.Gender, from.Gender, v => user.Gender = v);
        Fill(user.Nationality, from.Nationality, v => user.Nationality = v);
        Fill(user.PassportNumber, from.PassportNumber, v => user.PassportNumber = v);
        Fill(user.PassportIssueDate, from.PassportIssueDate, v => user.PassportIssueDate = v);
        Fill(user.PassportExpiryDate, from.PassportExpiryDate, v => user.PassportExpiryDate = v);
        Fill(user.PassportIssuingCountry, from.PassportIssuingCountry, v => user.PassportIssuingCountry = v);
        if (!changed)
            return Change.None;
        (await users.UpdateAsync(user)).ThrowIfFailed();
        return Change.Updated;
    }

    private static bool IsEmpty<T>(T value) => value is null || value is string s && string.IsNullOrWhiteSpace(s);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
