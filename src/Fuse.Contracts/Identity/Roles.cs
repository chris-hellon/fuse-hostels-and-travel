namespace Fuse.Contracts.Identity;

public sealed record RoleSummary(string Id, string Name, string? Description, bool IsDefault);

public sealed record UserRoleAssignment(string RoleId, string RoleName, string? Description, bool Assigned);

[Get("roles"), Permission(FuseAction.View, FuseResource.Roles)]
public sealed record ListRoles : IQuery<IReadOnlyList<RoleSummary>>;

[Post("roles"), Permission(FuseAction.Create, FuseResource.Roles)]
public sealed record CreateRole(string Name, string? Description) : ICommand<string>;

[Put("roles/{Id}"), Permission(FuseAction.Update, FuseResource.Roles)]
public sealed record UpdateRole(string Id, string Name, string? Description) : ICommand;

[Delete("roles/{Id}"), Permission(FuseAction.Delete, FuseResource.Roles)]
public sealed record DeleteRole(string Id) : ICommand;

/// <summary>The permission claim values (<c>Permissions.{Resource}.{Action}</c>) granted to a role.</summary>
[Get("roles/{RoleId}/permissions"), Permission(FuseAction.View, FuseResource.RoleClaims)]
public sealed record GetRolePermissions(string RoleId) : IQuery<IReadOnlyList<string>>;

[Put("roles/{RoleId}/permissions"), Permission(FuseAction.Update, FuseResource.RoleClaims)]
public sealed record UpdateRolePermissions(string RoleId, IReadOnlyList<string> Permissions) : ICommand;

[Get("users/{UserId}/roles"), Permission(FuseAction.View, FuseResource.UserRoles)]
public sealed record GetUserRoles(string UserId) : IQuery<IReadOnlyList<UserRoleAssignment>>;

/// <summary>Replaces the user's role set with <see cref="RoleIds"/>.</summary>
[Put("users/{UserId}/roles"), Permission(FuseAction.Update, FuseResource.UserRoles)]
public sealed record UpdateUserRoles(string UserId, IReadOnlyList<string> RoleIds) : ICommand;

public sealed class RoleNameValidator : AbstractValidator<CreateRole>
{
    public RoleNameValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
}

public sealed class UpdateRoleValidator : AbstractValidator<UpdateRole>
{
    public UpdateRoleValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
}
