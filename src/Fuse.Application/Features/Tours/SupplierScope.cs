namespace Fuse.Application.Features.Tours;

/// <summary>
/// Which tours the current user may see bookings for: everything, unless they're a supplier, in which
/// case only the tours they supply (Tour.SupplierId). A supplier with no tours sees nothing. (Legacy
/// passed an empty tour list to the procs, which they read as "no filter": suppliers without tours saw
/// every booking.)
/// </summary>
internal sealed class SupplierScope(IAppDbContext db, ICurrentUser user)
{
    /// <summary>Null = no restriction; otherwise the only tours allowed (possibly none).</summary>
    public async Task<IReadOnlyList<DefaultIdType>?> TourIdsAsync(CancellationToken cancellationToken)
    {
        if (!user.IsInRole(FuseRoles.Supplier) || user.IsInRole(FuseRoles.Admin))
            return null;
        var id = user.UserId?.ToString();
        if (id is null)
            return [];
        return await db.Tours.AsNoTracking().Where(t => t.SupplierId == id).Select(t => t.Id).ToListAsync(cancellationToken);
    }
}
