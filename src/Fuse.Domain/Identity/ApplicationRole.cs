using Microsoft.AspNetCore.Identity;

namespace Fuse.Domain.Identity;

public class ApplicationRole : IdentityRole
{
    private ApplicationRole() { }

    public ApplicationRole(string name, string? description = null)
        : base(name)
    {
        Description = description;
        NormalizedName = name.ToUpperInvariant();
    }

    public string? Description { get; set; }
}

public class ApplicationRoleClaim : IdentityRoleClaim<string>
{
    public string? CreatedBy { get; init; }
    public DateTime CreatedOn { get; init; }
}
