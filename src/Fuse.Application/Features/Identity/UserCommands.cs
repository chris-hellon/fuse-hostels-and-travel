using Microsoft.AspNetCore.Identity;
using Fuse.Contracts.Identity;
using Fuse.Domain.Identity;

namespace Fuse.Application.Features.Identity;

/// <summary>
/// Staff and guests share one user table. Each side's endpoints only ever touch its own kind of
/// user, so holding the Guests permission never exposes a staff account (and vice versa).
/// </summary>
internal sealed class UserKinds(IAppDbContext db)
{
    public Task<bool> IsStaffAsync(string userId, CancellationToken cancellationToken) =>
        db.UserRoles.Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name)
            .AnyAsync(name => FuseRoles.StaffRoles.Contains(name!), cancellationToken);

    public async Task<ApplicationUser> FindAsync(string userId, bool staff, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || await IsStaffAsync(userId, cancellationToken) != staff)
            throw new NotFoundException(staff ? "Staff member not found." : "Guest not found.");
        return user;
    }

    public async Task<UserDetails> GetDetailsAsync(string userId, bool staff, CancellationToken cancellationToken)
    {
        if (await IsStaffAsync(userId, cancellationToken) != staff)
            throw new NotFoundException(staff ? "Staff member not found." : "Guest not found.");
        return await db.Users.AsNoTracking().Where(u => u.Id == userId).ToDetails().FirstOrDefaultAsync(cancellationToken)
               ?? throw new NotFoundException(staff ? "Staff member not found." : "Guest not found.");
    }
}

internal static class IdentityResults
{
    public static void ThrowIfFailed(this IdentityResult result, string property = "")
    {
        if (result.Succeeded)
            return;

        var errors = result.Errors
            .GroupBy(e => e.Code.Contains("Email", StringComparison.OrdinalIgnoreCase) ? "Profile.Email"
                : e.Code.Contains("Password", StringComparison.OrdinalIgnoreCase) ? "Password"
                : property)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
        throw new RequestValidationException(errors);
    }

    public static ApplicationUser NewUser(UserProfile profile)
    {
        var user = new ApplicationUser
        {
            IsActive = true,
            EmailConfirmed = true,
            LockoutEnabled = true,
            SignUpDate = DateTime.UtcNow,
            RefreshTokenExpiryTime = DateTime.UtcNow,
        };
        user.Apply(profile);
        // Guests created at the desk or checkout may have no email: Apply gave them a placeholder, which is unique.
        user.UserName = user.Email;
        return user;
    }
}

internal sealed class GetStaffHandler(UserKinds kinds) : IQueryHandler<GetStaff, UserDetails>
{
    public async ValueTask<UserDetails> Handle(GetStaff query, CancellationToken cancellationToken) =>
        await kinds.GetDetailsAsync(query.Id, staff: true, cancellationToken);
}

internal sealed class GetGuestHandler(UserKinds kinds) : IQueryHandler<GetGuest, UserDetails>
{
    public async ValueTask<UserDetails> Handle(GetGuest query, CancellationToken cancellationToken) =>
        await kinds.GetDetailsAsync(query.Id, staff: false, cancellationToken);
}

internal sealed class CreateStaffHandler(UserManager<ApplicationUser> users) : ICommandHandler<CreateStaff, string>
{
    public async ValueTask<string> Handle(CreateStaff command, CancellationToken cancellationToken)
    {
        var user = IdentityResults.NewUser(command.Profile);
        (await users.CreateAsync(user, command.Password)).ThrowIfFailed();
        // New staff start with the Basic role, as before; more roles are granted on the Roles screen.
        (await users.AddToRoleAsync(user, FuseRoles.Basic)).ThrowIfFailed();
        return user.Id;
    }
}

internal sealed class CreateSupplierHandler(UserManager<ApplicationUser> users) : ICommandHandler<CreateSupplier, string>
{
    public async ValueTask<string> Handle(CreateSupplier command, CancellationToken cancellationToken)
    {
        var user = IdentityResults.NewUser(command.Profile);
        (await users.CreateAsync(user, command.Password)).ThrowIfFailed();
        (await users.AddToRoleAsync(user, FuseRoles.Supplier)).ThrowIfFailed();
        return user.Id;
    }
}

internal sealed class CreateGuestHandler(UserManager<ApplicationUser> users) : ICommandHandler<CreateGuest, string>
{
    public async ValueTask<string> Handle(CreateGuest command, CancellationToken cancellationToken)
    {
        var user = IdentityResults.NewUser(command.Profile);
        (await users.CreateAsync(user)).ThrowIfFailed();
        (await users.AddToRoleAsync(user, FuseRoles.Guest)).ThrowIfFailed();
        return user.Id;
    }
}

internal sealed class UpdateStaffHandler(UserKinds kinds, UserManager<ApplicationUser> users) : ICommandHandler<UpdateStaff>
{
    public async ValueTask<Unit> Handle(UpdateStaff command, CancellationToken cancellationToken)
    {
        var user = await kinds.FindAsync(command.Id, staff: true, cancellationToken);
        user.Apply(command.Profile);
        (await users.UpdateAsync(user)).ThrowIfFailed();
        return Unit.Value;
    }
}

internal sealed class UpdateGuestHandler(UserKinds kinds, UserManager<ApplicationUser> users) : ICommandHandler<UpdateGuest>
{
    public async ValueTask<Unit> Handle(UpdateGuest command, CancellationToken cancellationToken)
    {
        var user = await kinds.FindAsync(command.Id, staff: false, cancellationToken);
        user.Apply(command.Profile);
        (await users.UpdateAsync(user)).ThrowIfFailed();
        return Unit.Value;
    }
}

internal sealed class SetStaffActiveHandler(UserKinds kinds, UserManager<ApplicationUser> users) : ICommandHandler<SetStaffActive>
{
    public async ValueTask<Unit> Handle(SetStaffActive command, CancellationToken cancellationToken)
    {
        var user = await kinds.FindAsync(command.Id, staff: true, cancellationToken);
        if (await users.IsInRoleAsync(user, FuseRoles.Admin))
            throw new BusinessRuleException("Administrators can't be deactivated.");

        user.IsActive = command.IsActive;
        (await users.UpdateAsync(user)).ThrowIfFailed();
        return Unit.Value;
    }
}

internal sealed class SetStaffPasswordHandler(UserKinds kinds, UserManager<ApplicationUser> users) : ICommandHandler<SetStaffPassword>
{
    public async ValueTask<Unit> Handle(SetStaffPassword command, CancellationToken cancellationToken)
    {
        var user = await kinds.FindAsync(command.Id, staff: true, cancellationToken);
        // Same rules as sign-up, then replace the hash in one update (no window without a password).
        foreach (var validator in users.PasswordValidators)
            (await validator.ValidateAsync(users, user, command.Password)).ThrowIfFailed("Password");
        user.PasswordHash = users.PasswordHasher.HashPassword(user, command.Password);
        await users.UpdateSecurityStampAsync(user); // signs them out elsewhere; also saves the user
        return Unit.Value;
    }
}
