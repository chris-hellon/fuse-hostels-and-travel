namespace Fuse.Application.Common.Persistence;

/// <summary>The current tenant, for raw SQL (stored procedures take <c>@TenantId</c>).</summary>
public interface ITenantContext
{
    /// <summary>The tenant's Id ("fuse"), which is what TenantId columns hold. Not its Identifier.</summary>
    string TenantId { get; }
}
