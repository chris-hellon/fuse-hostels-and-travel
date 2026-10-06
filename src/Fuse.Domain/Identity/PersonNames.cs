namespace Fuse.Domain.Identity;

/// <summary>
/// A person's display name, as legacy showed it: first and last name (each trimmed, one space between), never the
/// stored <see cref="ApplicationUser.FullName"/>, which is stale for many older accounts. The stored column is only
/// the fallback when both names are empty. FullName stays the search/sort column (indexed, used by the procs) and is
/// kept in step on every write with <see cref="SyncFullName"/>.
/// <para>In EF queries, project the three columns and call <see cref="Display(string?, string?, string?)"/> in the
/// final Select (EF evaluates it client-side after reading them); never use it in a Where/OrderBy.</para>
/// </summary>
public static class PersonNames
{
    public static string Display(string? firstName, string? lastName, string? fullName = null)
    {
        var first = firstName?.Trim();
        var last = lastName?.Trim();
        var name = string.IsNullOrEmpty(first) ? last ?? ""
            : string.IsNullOrEmpty(last) ? first
            : $"{first} {last}";
        return name.Length > 0 ? name : fullName?.Trim() ?? "";
    }

    public static string DisplayName(this ApplicationUser user) => Display(user.FirstName, user.LastName, user.FullName);

    /// <summary>Sets the stored FullName from the first and last name (kept as is when both are empty).</summary>
    public static void SyncFullName(this ApplicationUser user)
    {
        var name = user.DisplayName();
        user.FullName = name.Length > 0 ? name : null;
    }
}
