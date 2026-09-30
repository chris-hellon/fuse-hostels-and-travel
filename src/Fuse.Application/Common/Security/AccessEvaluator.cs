namespace Fuse.Application.Common.Security;

public enum AccessResult
{
    Allowed,
    Unauthenticated,
    Forbidden,
}

/// <summary>Decides whether the current user may send a request, from its contract's access attribute.</summary>
public interface IAccessEvaluator
{
    ValueTask<AccessResult> EvaluateAsync(Type requestType, CancellationToken cancellationToken = default);
}

internal sealed class AccessEvaluator(ICurrentUser currentUser, IPermissionService permissions) : IAccessEvaluator
{
    public async ValueTask<AccessResult> EvaluateAsync(Type requestType, CancellationToken cancellationToken = default)
    {
        var metadata = ContractMetadata.For(requestType);

        // Server-only messages (not exposed over HTTP) are trusted: only server code can send them.
        if (metadata.Access is null)
            return metadata.IsExposed ? AccessResult.Forbidden : AccessResult.Allowed;

        if (metadata.Access is PublicAttribute)
            return AccessResult.Allowed;

        if (!currentUser.IsAuthenticated)
            return AccessResult.Unauthenticated;

        switch (metadata.Access)
        {
            case AuthenticatedAttribute:
                return AccessResult.Allowed;

            case StaffAttribute:
                return IsStaff() ? AccessResult.Allowed : AccessResult.Forbidden;

            case PermissionAttribute permission:
                if (!IsStaff() || currentUser.UserId is not { } userId)
                    return AccessResult.Forbidden;
                var granted = await permissions.GetPermissionsAsync(userId.ToString(), cancellationToken);
                return granted.Contains(permission.Name) ? AccessResult.Allowed : AccessResult.Forbidden;

            default:
                return AccessResult.Forbidden;
        }
    }

    private bool IsStaff() => FuseRoles.StaffRoles.Any(currentUser.IsInRole);
}
