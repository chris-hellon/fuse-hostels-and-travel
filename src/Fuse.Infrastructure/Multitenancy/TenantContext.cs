using Finbuckle.MultiTenant.Abstractions;
using Fuse.Application.Common.Persistence;

namespace Fuse.Infrastructure.Multitenancy;

internal sealed class TenantContext(IMultiTenantContextAccessor<FuseTenantInfo> accessor) : ITenantContext
{
    public string TenantId => accessor.MultiTenantContext.TenantInfo?.Id
        ?? throw new InvalidOperationException("No tenant has been resolved for this request.");
}
