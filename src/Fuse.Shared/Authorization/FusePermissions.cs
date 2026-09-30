namespace Fuse.Shared.Authorization;

public static class FuseAction
{
    public const string View = nameof(View);
    public const string Search = nameof(Search);
    public const string Create = nameof(Create);
    public const string Update = nameof(Update);
    public const string Delete = nameof(Delete);
    public const string Export = nameof(Export);
    public const string UpgradeSubscription = nameof(UpgradeSubscription);
}

public static class FuseResource
{
    public const string Tenants = nameof(Tenants);
    public const string Dashboard = nameof(Dashboard);
    public const string Calendar = nameof(Calendar);
    public const string Hangfire = nameof(Hangfire);
    public const string Users = nameof(Users);
    public const string UserRoles = nameof(UserRoles);
    public const string Roles = nameof(Roles);
    public const string RoleClaims = nameof(RoleClaims);
    public const string Locations = nameof(Locations);
    public const string Tours = nameof(Tours);
    public const string Packages = nameof(Packages);
    public const string PackIncentives = nameof(PackIncentives);
    public const string Guests = nameof(Guests);
    public const string Suppliers = nameof(Suppliers);
    public const string Properties = nameof(Properties);
    public const string TourBookings = nameof(TourBookings);
    public const string PropertyBookings = nameof(PropertyBookings);
    public const string Bookings = nameof(Bookings);
    public const string Services = nameof(Services);
    public const string Events = nameof(Events);
    public const string JobVacancies = nameof(JobVacancies);
    public const string Enquiries = nameof(Enquiries);
    public const string Pages = nameof(Pages);
    public const string TravelGuides = nameof(TravelGuides);
    public const string Gallery = nameof(Gallery);
    public const string Settings = nameof(Settings);
    public const string Seo = nameof(Seo);
    public const string Reports = nameof(Reports);
}

/// <summary>Which built-in roles are granted a permission by default (Admin gets every non-root permission).</summary>
[Flags]
public enum DefaultGrant
{
    None = 0,
    Basic = 1,
    TourManager = 2,
    PropertyManager = 4,
    Supplier = 8,
    Root = 16,
}

public sealed record FusePermission(string Description, string Action, string Resource, DefaultGrant Grants)
{
    /// <summary>Stored as the role-claim value, so the format must never change.</summary>
    public string Name => NameFor(Action, Resource);

    public bool IsRoot => Grants.HasFlag(DefaultGrant.Root);

    public static string NameFor(string action, string resource) => $"Permissions.{resource}.{action}";
}

public static class FusePermissions
{
    private const DefaultGrant B = DefaultGrant.Basic;
    private const DefaultGrant T = DefaultGrant.TourManager;
    private const DefaultGrant P = DefaultGrant.PropertyManager;

    private static readonly FusePermission[] _all =
    [
        One("View Dashboard", FuseAction.View, FuseResource.Dashboard),
        One("View Calendar", FuseAction.View, FuseResource.Calendar, B | DefaultGrant.Supplier),
        One("View Hangfire", FuseAction.View, FuseResource.Hangfire),

        ..Crud("Reports", FuseResource.Reports, view: DefaultGrant.None, manage: DefaultGrant.None, export: true, create: true),
        ..Crud("Users", FuseResource.Users, view: DefaultGrant.None, manage: DefaultGrant.None, export: true),
        One("View UserRoles", FuseAction.View, FuseResource.UserRoles),
        One("Update UserRoles", FuseAction.Update, FuseResource.UserRoles),
        One("View Roles", FuseAction.View, FuseResource.Roles),
        One("Create Roles", FuseAction.Create, FuseResource.Roles),
        One("Update Roles", FuseAction.Update, FuseResource.Roles),
        One("Delete Roles", FuseAction.Delete, FuseResource.Roles),
        One("View RoleClaims", FuseAction.View, FuseResource.RoleClaims),
        One("Update RoleClaims", FuseAction.Update, FuseResource.RoleClaims),

        ..Crud("Locations", FuseResource.Locations, view: B | P, manage: P, export: true),
        ..Crud("Tours", FuseResource.Tours, view: B | T, manage: T, export: true),
        ..Crud("Packages", FuseResource.Packages, view: B | T, manage: T),
        ..Crud("Pack Incentives", FuseResource.PackIncentives, view: B | T, manage: T),
        ..Crud("Guests", FuseResource.Guests, view: B | T | P, manage: T | P, export: true),
        ..Crud("Suppliers", FuseResource.Suppliers, view: B, manage: DefaultGrant.None, export: true),
        ..Crud("Properties", FuseResource.Properties, view: B | P, manage: P, export: true),
        ..Crud("Bookings", FuseResource.Bookings, view: B | T | P, manage: T | P, export: true),
        ..Crud("Tour Bookings", FuseResource.TourBookings, view: B | T, manage: T, export: true),
        ..Crud("Property Bookings", FuseResource.PropertyBookings, view: B | P, manage: P, export: true),
        ..Crud("Services", FuseResource.Services, view: B, manage: DefaultGrant.None, export: true),
        ..Crud("Events", FuseResource.Events, view: B, manage: DefaultGrant.None, export: true),
        ..Crud("Job Vacancies", FuseResource.JobVacancies, view: B, manage: DefaultGrant.None, export: true),
        ..Crud("Enquiries", FuseResource.Enquiries, view: B, manage: DefaultGrant.None, export: true),
        ..Crud("Pages", FuseResource.Pages, view: B, manage: DefaultGrant.None, export: true),
        ..Crud("Travel Guides", FuseResource.TravelGuides, view: B, manage: DefaultGrant.None, export: true),
        ..Crud("Gallery", FuseResource.Gallery, view: B, manage: DefaultGrant.None, export: true),

        One("View Settings", FuseAction.View, FuseResource.Settings),
        One("Update Settings", FuseAction.Update, FuseResource.Settings),
        ..Crud("Seo", FuseResource.Seo, view: DefaultGrant.None, manage: DefaultGrant.None, export: true),

        One("View Tenants", FuseAction.View, FuseResource.Tenants, DefaultGrant.Root),
        One("Create Tenants", FuseAction.Create, FuseResource.Tenants, DefaultGrant.Root),
        One("Update Tenants", FuseAction.Update, FuseResource.Tenants, DefaultGrant.Root),
        One("Upgrade Tenant Subscription", FuseAction.UpgradeSubscription, FuseResource.Tenants, DefaultGrant.Root),
    ];

    public static IReadOnlyList<FusePermission> All { get; } = _all;
    public static IReadOnlyList<FusePermission> Root { get; } = [.. _all.Where(p => p.IsRoot)];
    public static IReadOnlyList<FusePermission> Admin { get; } = [.. _all.Where(p => !p.IsRoot)];
    public static IReadOnlyList<FusePermission> Basic { get; } = For(DefaultGrant.Basic);
    public static IReadOnlyList<FusePermission> TourManager { get; } = For(DefaultGrant.TourManager);
    public static IReadOnlyList<FusePermission> PropertyManager { get; } = For(DefaultGrant.PropertyManager);
    public static IReadOnlyList<FusePermission> Supplier { get; } = For(DefaultGrant.Supplier);

    private static FusePermission[] For(DefaultGrant grant) => [.. _all.Where(p => p.Grants.HasFlag(grant))];

    private static FusePermission One(string description, string action, string resource, DefaultGrant grants = DefaultGrant.None) =>
        new(description, action, resource, grants);

    /// <summary>View + Search granted to <paramref name="view"/>; Create/Update/Delete (+Export) to <paramref name="manage"/>.</summary>
    private static IEnumerable<FusePermission> Crud(string label, string resource, DefaultGrant view, DefaultGrant manage,
        bool export = false, bool create = true)
    {
        yield return One($"View {label}", FuseAction.View, resource, view);
        yield return One($"Search {label}", FuseAction.Search, resource, view);
        if (create) yield return One($"Create {label}", FuseAction.Create, resource, manage);
        yield return One($"Update {label}", FuseAction.Update, resource, manage);
        yield return One($"Delete {label}", FuseAction.Delete, resource, manage);
        if (export) yield return One($"Export {label}", FuseAction.Export, resource, manage);
    }
}
