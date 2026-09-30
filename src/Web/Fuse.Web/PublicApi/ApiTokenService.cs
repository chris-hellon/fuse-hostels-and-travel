using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Fuse.Domain.Identity;
using Fuse.Shared.Authorization;

namespace Fuse.Web.PublicApi;

public sealed record TokenRequest(string ClientId, string ClientSecret);

public sealed record TokenResult(string AccessToken, string TokenType, int ExpiresIn);

public enum TokenStatus
{
    Success,
    InvalidClient,
    Forbidden,
}

/// <summary>Legacy ApiTokenService: a client's credentials for a JWT acting as its service staff user.</summary>
internal sealed class ApiTokenService(IOptions<ApiSettings> settings, UserManager<ApplicationUser> users)
{
    public async Task<(TokenStatus Status, TokenResult? Token)> IssueAsync(TokenRequest request)
    {
        var client = ValidClient(request.ClientId, request.ClientSecret);
        if (client is null)
            return (TokenStatus.InvalidClient, null);
        if (string.IsNullOrWhiteSpace(client.UserId))
            return (TokenStatus.Forbidden, null);

        var user = await users.FindByIdAsync(client.UserId);
        if (user is null || !user.IsActive)
            return (TokenStatus.Forbidden, null);

        // Only staff may use the API.
        var roles = await users.GetRolesAsync(user);
        if (!roles.Any(r => FuseRoles.StaffRoles.Contains(r, StringComparer.OrdinalIgnoreCase)))
            return (TokenStatus.Forbidden, null);

        return (TokenStatus.Success, Token(user, roles));
    }

    private ApiClient? ValidClient(string? clientId, string? clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return null;
        var client = settings.Value.Clients.FirstOrDefault(c => string.Equals(c.ClientId, clientId, StringComparison.Ordinal));
        // Constant-time comparison; never reveal whether the id or the secret was wrong.
        return client is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(client.ClientSecret), Encoding.UTF8.GetBytes(clientSecret))
            ? client
            : null;
    }

    private TokenResult Token(ApplicationUser user, IEnumerable<string> roles)
    {
        var jwt = settings.Value.Jwt;
        var lifetime = TimeSpan.FromMinutes(jwt.TokenLifetimeMinutes);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.FullName ?? user.UserName ?? user.Email ?? user.Id),
            new(ClaimTypes.Email, user.Email ?? ""),
            new(FuseClaims.Fullname, user.FullName ?? ""),
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Expires = DateTime.UtcNow.Add(lifetime),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)), SecurityAlgorithms.HmacSha256),
        });
        return new TokenResult(token, "Bearer", (int)lifetime.TotalSeconds);
    }
}
