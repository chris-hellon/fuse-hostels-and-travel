namespace Fuse.Shared.Authorization;

public static class FuseRoles
{
    public const string Admin = nameof(Admin);
    public const string Basic = nameof(Basic);
    public const string Guest = nameof(Guest);
    public const string TourManager = nameof(TourManager);
    public const string PropertyManager = nameof(PropertyManager);
    public const string Supplier = nameof(Supplier);

    public static IReadOnlyList<string> DefaultRoles { get; } = [Admin, Basic, Guest, TourManager, PropertyManager, Supplier];

    /// <summary>Roles that may use the back office. Everyone else (Guest) is a public-site customer.</summary>
    public static IReadOnlyList<string> StaffRoles { get; } = [Admin, Basic, TourManager, PropertyManager, Supplier];

    public static bool IsDefault(string roleName) => DefaultRoles.Contains(roleName);
}
