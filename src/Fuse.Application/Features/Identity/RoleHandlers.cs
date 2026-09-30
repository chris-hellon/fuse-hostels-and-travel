using Microsoft.AspNetCore.Identity;
using Fuse.Contracts.Identity;
using Fuse.Domain.Identity;

namespace Fuse.Application.Features.Identity;

internal sealed class ListRolesHandler(IAppDbContext db) : IQueryHandler<ListRoles, IReadOnlyList<RoleSummary>>
{
    public async ValueTask<IReadOnlyList<RoleSummary>> Handle(ListRoles query, CancellationToken cancellationToken)
    {
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken);
        return [.. roles.Select(r => new RoleSummary(r.Id, r.Name!, r.Description, FuseRoles.IsDefault(r.Name!)))];
    }
}

internal sealed class CreateRoleHandler(RoleManager<ApplicationRole> roles) : ICommandHandler<CreateRole, string>
{
    public async ValueTask<string> Handle(CreateRole command, CancellationToken cancellationToken)
    {
        var role = new ApplicationRole(command.Name.Trim(), command.Description);
        (await roles.CreateAsync(role)).ThrowIfFailed(nameof(CreateRole.Name));
        return role.Id;
    }
}

internal sealed class UpdateRoleHandler(RoleManager<ApplicationRole> roles) : ICommandHandler<UpdateRole>
{
    public async ValueTask<Unit> Handle(UpdateRole command, CancellationToken cancellationToken)
    {
        var role = await roles.FindByIdAsync(command.Id) ?? throw new NotFoundException("Role not found.");
        // Built-in role names are referenced in code (seeding, staff checks); only their description can change.
        if (FuseRoles.IsDefault(role.Name!) && role.Name != command.Name.Trim())
            throw new BusinessRuleException($"The {role.Name} role is built in and can't be renamed.");

        role.Name = command.Name.Trim();
        role.Description = command.Description;
        (await roles.UpdateAsync(role)).ThrowIfFailed(nameof(UpdateRole.Name));
        return Unit.Value;
    }
}

internal sealed class DeleteRoleHandler(IAppDbContext db, RoleManager<ApplicationRole> roles) : ICommandHandler<DeleteRole>
{
    public async ValueTask<Unit> Handle(DeleteRole command, CancellationToken cancellationToken)
    {
        var role = await roles.FindByIdAsync(command.Id) ?? throw new NotFoundException("Role not found.");
        if (FuseRoles.IsDefault(role.Name!))
            throw new BusinessRuleException($"The {role.Name} role is built in and can't be deleted.");
        if (await db.UserRoles.AnyAsync(ur => ur.RoleId == role.Id, cancellationToken))
            throw new BusinessRuleException($"The {role.Name} role is assigned to people. Remove it from them first.");

        (await roles.DeleteAsync(role)).ThrowIfFailed();
        return Unit.Value;
    }
}

internal sealed class GetRolePermissionsHandler(IAppDbContext db) : IQueryHandler<GetRolePermissions, IReadOnlyList<string>>
{
    public async ValueTask<IReadOnlyList<string>> Handle(GetRolePermissions query, CancellationToken cancellationToken)
    {
        if (!await db.Roles.AnyAsync(r => r.Id == query.RoleId, cancellationToken))
            throw new NotFoundException("Role not found.");

        return await db.RoleClaims
            .Where(c => c.RoleId == query.RoleId && c.ClaimType == FuseClaims.Permission && c.ClaimValue != null)
            .Select(c => c.ClaimValue!)
            .ToListAsync(cancellationToken);
    }
}

internal sealed class UpdateRolePermissionsHandler(IAppDbContext db, ICurrentUser currentUser, IPermissionService permissions)
    : ICommandHandler<UpdateRolePermissions>
{
    public async ValueTask<Unit> Handle(UpdateRolePermissions command, CancellationToken cancellationToken)
    {
        if (!await db.Roles.AnyAsync(r => r.Id == command.RoleId, cancellationToken))
            throw new NotFoundException("Role not found.");

        // Only real, non-root permissions can be granted; anything else is ignored rather than stored.
        var grantable = FusePermissions.Admin.Select(p => p.Name).ToHashSet();
        var wanted = command.Permissions.Where(grantable.Contains).ToHashSet();

        var current = await db.RoleClaims
            .Where(c => c.RoleId == command.RoleId && c.ClaimType == FuseClaims.Permission)
            .ToListAsync(cancellationToken);

        db.RoleClaims.RemoveRange(current.Where(c => !wanted.Contains(c.ClaimValue!)));
        foreach (var permission in wanted.Except(current.Select(c => c.ClaimValue!)))
        {
            db.RoleClaims.Add(new ApplicationRoleClaim
            {
                RoleId = command.RoleId,
                ClaimType = FuseClaims.Permission,
                ClaimValue = permission,
                CreatedBy = currentUser.UserId?.ToString(),
                CreatedOn = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await permissions.InvalidateRoleAsync(command.RoleId, cancellationToken);
        return Unit.Value;
    }
}

internal sealed class GetUserRolesHandler(IAppDbContext db) : IQueryHandler<GetUserRoles, IReadOnlyList<UserRoleAssignment>>
{
    public async ValueTask<IReadOnlyList<UserRoleAssignment>> Handle(GetUserRoles query, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(u => u.Id == query.UserId, cancellationToken))
            throw new NotFoundException("User not found.");

        var assigned = await db.UserRoles.Where(ur => ur.UserId == query.UserId).Select(ur => ur.RoleId).ToListAsync(cancellationToken);
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken);
        return [.. roles.Select(r => new UserRoleAssignment(r.Id, r.Name!, r.Description, assigned.Contains(r.Id)))];
    }
}

internal sealed class UpdateUserRolesHandler(IAppDbContext db, IPermissionService permissions) : ICommandHandler<UpdateUserRoles>
{
    /// <summary>Removing admin rights is refused if it would leave fewer than this many admins.</summary>
    private const int MinimumAdmins = 2;

    public async ValueTask<Unit> Handle(UpdateUserRoles command, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(u => u.Id == command.UserId, cancellationToken))
            throw new NotFoundException("User not found.");

        var roles = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name!, cancellationToken);
        var wanted = command.RoleIds.Where(roles.ContainsKey).ToHashSet();
        var current = await db.UserRoles.Where(ur => ur.UserId == command.UserId).ToListAsync(cancellationToken);

        var adminRoleId = roles.FirstOrDefault(r => r.Value == FuseRoles.Admin).Key;
        if (adminRoleId is not null && current.Any(ur => ur.RoleId == adminRoleId) && !wanted.Contains(adminRoleId))
        {
            var admins = await db.UserRoles.CountAsync(ur => ur.RoleId == adminRoleId, cancellationToken);
            if (admins <= MinimumAdmins)
                throw new BusinessRuleException($"There must always be at least {MinimumAdmins} administrators.");
        }

        db.UserRoles.RemoveRange(current.Where(ur => !wanted.Contains(ur.RoleId)));
        foreach (var roleId in wanted.Except(current.Select(ur => ur.RoleId)))
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = command.UserId, RoleId = roleId });

        await db.SaveChangesAsync(cancellationToken);
        permissions.Invalidate(command.UserId);
        return Unit.Value;
    }
}
