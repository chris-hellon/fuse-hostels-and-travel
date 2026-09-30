using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Fuse.Domain.Identity;
using Fuse.Infrastructure.Persistence;

namespace Fuse.Infrastructure.Identity;

// The legacy database has thousands of accounts sharing an email (couples, groups and repeat stays imported from
// Cloudbeds, which legacy wrote straight to the table) plus some usernames Identity wouldn't accept today. Legacy's
// admin wrote users without Identity's checks, so those accounts could always be signed in to, edited, deactivated
// and given passwords. These two replacements keep that working without letting anyone create a new clash.

/// <summary>
/// The EF user store, except that an email shared by several accounts finds one of them instead of throwing (the stock
/// store uses SingleOrDefault). Preference: the account that signs in with that address (its username), then one with a
/// password, then an active one.
/// </summary>
internal sealed class LegacyTolerantUserStore(AppDbContext context, IdentityErrorDescriber? describer = null)
    : UserStore<ApplicationUser, ApplicationRole, AppDbContext, string, IdentityUserClaim<string>, IdentityUserRole<string>,
        IdentityUserLogin<string>, IdentityUserToken<string>, ApplicationRoleClaim, IdentityUserPasskey<string>>(context, describer)
{
    public override Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Users.Where(u => u.NormalizedEmail == normalizedEmail)
            .OrderByDescending(u => u.NormalizedUserName == normalizedEmail)
            .ThenByDescending(u => u.PasswordHash != null)
            .ThenByDescending(u => u.IsActive)
            .ThenBy(u => u.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}

/// <summary>
/// Identity's user validator (same rules and messages), applied only to what a write changes: a new account, a new
/// username or a new email must be valid and not already someone else's, but an account keeps the (shared, blank or
/// unusual) email and username it already has when anything else about it is saved.
/// </summary>
internal sealed class LegacyTolerantUserValidator(AppDbContext db, IdentityErrorDescriber describer) : IUserValidator<ApplicationUser>
{
    public async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        var stored = await db.Users.AsNoTracking().Where(u => u.Id == user.Id)
            .Select(u => new { u.UserName, u.Email }).FirstOrDefaultAsync();
        var errors = new List<IdentityError>();

        if (stored is null || !string.Equals(stored.UserName, user.UserName, StringComparison.Ordinal))
            await ValidateUserNameAsync(manager, user, errors);
        if (manager.Options.User.RequireUniqueEmail && (stored is null || !string.Equals(stored.Email, user.Email, StringComparison.OrdinalIgnoreCase)))
            await ValidateEmailAsync(manager, user, errors);

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }

    private async Task ValidateUserNameAsync(UserManager<ApplicationUser> manager, ApplicationUser user, List<IdentityError> errors)
    {
        var userName = user.UserName;
        var allowed = manager.Options.User.AllowedUserNameCharacters;
        if (string.IsNullOrWhiteSpace(userName) || !string.IsNullOrEmpty(allowed) && userName.Any(c => !allowed.Contains(c)))
        {
            errors.Add(describer.InvalidUserName(userName));
            return;
        }

        var normalized = manager.NormalizeName(userName);
        if (await db.Users.AnyAsync(u => u.NormalizedUserName == normalized && u.Id != user.Id))
            errors.Add(describer.DuplicateUserName(userName));
    }

    private async Task ValidateEmailAsync(UserManager<ApplicationUser> manager, ApplicationUser user, List<IdentityError> errors)
    {
        var email = user.Email;
        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
        {
            errors.Add(describer.InvalidEmail(email));
            return;
        }

        var normalized = manager.NormalizeEmail(email);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized && u.Id != user.Id))
            errors.Add(describer.DuplicateEmail(email));
    }
}
