using Finbuckle.MultiTenant.Abstractions;

namespace Fuse.Infrastructure.Multitenancy;

/// <summary>
/// Row in <c>MultiTenancy.Tenants</c>. Branding columns are retained for schema compatibility;
/// the Fuse site's look now lives in its own stylesheet.
/// </summary>
public sealed class FuseTenantInfo : ITenantInfo
{
    public required string Id { get; init; }
    public required string Identifier { get; init; }
    public string Name { get; set; } = null!;
    public string ConnectionString { get; set; } = string.Empty;
    public string AdminEmail { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTime ValidUpto { get; set; }
    public string? Issuer { get; set; }
    public string? Url { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? LogoImageUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? PrimaryHoverColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string? SecondaryHoverColor { get; set; }
    public string? TeritaryColor { get; set; }
    public string? TeritaryHoverColor { get; set; }
    public string? HeaderFontWoffUrl { get; set; }
    public string? HeaderFontWoff2Url { get; set; }
    public string? HeaderFont { get; set; }
    public string? BodyFontWoffUrl { get; set; }
    public string? BodyFontWoff2Url { get; set; }
    public string? BodyFont { get; set; }
}
