using Fuse.Shared.Authorization;

namespace Fuse.Admin;

/// <summary>For <c>[Authorize(Roles = ...)]</c>, which needs a constant.</summary>
public static class StaffRoles
{
    public const string All = $"{FuseRoles.Admin},{FuseRoles.Basic},{FuseRoles.TourManager},{FuseRoles.PropertyManager},{FuseRoles.Supplier}";
}
