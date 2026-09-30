namespace Fuse.Contracts.Api;

/// <summary>
/// Who may send a request. Every contract must carry exactly one of these; the host refuses
/// to start otherwise. Enforced on the HTTP endpoint and again in the in-process pipeline.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public abstract class AccessAttribute : Attribute;

/// <summary>Anyone, signed in or not.</summary>
public sealed class PublicAttribute : AccessAttribute;

/// <summary>Any signed-in user, including public-site guests. The handler must scope data to the caller.</summary>
public sealed class AuthenticatedAttribute : AccessAttribute;

/// <summary>Any back-office user (a staff role), with no specific permission.</summary>
public sealed class StaffAttribute : AccessAttribute;

/// <summary>A staff user holding a specific permission.</summary>
public sealed class PermissionAttribute(string action, string resource) : AccessAttribute
{
    public string Action { get; } = action;
    public string Resource { get; } = resource;
    public string Name => FusePermission.NameFor(Action, Resource);
}
