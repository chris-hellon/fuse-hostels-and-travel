using Finbuckle.MultiTenant.Abstractions;

namespace Fuse.Infrastructure.Multitenancy;

/// <summary>
/// A tenant context with no HTTP request behind it: design-time tooling, background jobs and
/// tests. Web requests resolve the tenant through Finbuckle's middleware instead.
/// </summary>
public sealed class FixedTenantContextAccessor(FuseTenantInfo tenant) : IMultiTenantContextAccessor<FuseTenantInfo>
{
    public IMultiTenantContext<FuseTenantInfo> MultiTenantContext { get; } = new MultiTenantContext<FuseTenantInfo>(tenant);

    IMultiTenantContext IMultiTenantContextAccessor.MultiTenantContext => MultiTenantContext;
}
