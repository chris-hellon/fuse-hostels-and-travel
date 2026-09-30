namespace Fuse.Shared.Multitenancy;

public static class MultitenancyConstants
{
    public const string TenantIdName = "tenant";

    public static class Root
    {
        public const string Id = "root";
        public const string Name = "Root";
        public const string EmailAddress = "admin@root.com";
    }

    /// <summary>
    /// The single deployed tenant. Queries scope by <see cref="Id"/>; the static resolution
    /// strategy matches <see cref="Identifier"/>. They are different values.
    /// </summary>
    public static class Fuse
    {
        public const string Id = "fuse";
        public const string Identifier = "fusehostelsandtravel";
    }
}
