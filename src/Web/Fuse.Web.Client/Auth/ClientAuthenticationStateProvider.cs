using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Fuse.Contracts.Api;
using Fuse.Contracts.Auth;

namespace Fuse.Web.Client.Auth;

/// <summary>
/// Starts from the state the server serialized during prerendering, and can re-read the user from
/// <c>/api/auth/me</c> after sign-in/out so the UI updates without reloading.
/// </summary>
internal sealed class ClientAuthenticationStateProvider(AuthenticationStateProvider prerendered, HttpClient http)
    : AuthenticationStateProvider
{
    private Task<AuthenticationState>? _current;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        _current ?? prerendered.GetAuthenticationStateAsync();

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var info = await http.GetFromJsonAsync<CurrentUserInfo>(AuthRoutes.Me, ApiJson.Options, cancellationToken)
                   ?? CurrentUserInfo.Anonymous;
        Set(info);
    }

    public void Set(CurrentUserInfo info)
    {
        var identity = info.IsAuthenticated
            ? new ClaimsIdentity(info.Claims.Select(c => new Claim(c.Type, c.Value)), "Cookie", ClaimTypes.Name, ClaimTypes.Role)
            : new ClaimsIdentity();

        _current = Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        NotifyAuthenticationStateChanged(_current);
    }
}
