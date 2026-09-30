using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Fuse.Contracts.Api;
using Fuse.Contracts.Common;
using Fuse.Site.Interop;

namespace Fuse.Site.Components.Account;

/// <summary>
/// A My Account page: its data loads once the visitor is signed in (while prerendering, or straight after they sign
/// in from the page), and again if someone else signs in.
/// </summary>
public abstract class AccountPage : JsComponent
{
    [Inject] protected IApi Api { get; set; } = null!;
    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    private string? _loadedFor;

    /// <summary>Loads the page's data for the signed-in visitor.</summary>
    protected abstract Task LoadAsync();

    protected override async Task OnParametersSetAsync()
    {
        var user = AuthState is null ? null : (await AuthState).User;
        var id = user?.Identity?.IsAuthenticated == true ? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? user.Identity.Name : null;
        if (id is null || id == _loadedFor && !Reload)
            return;
        Reload = false;
        try
        {
            await LoadAsync();
            _loadedFor = id;
        }
        catch (UnauthorizedException)
        {
            // Signed out elsewhere: the shell shows the sign-in prompt.
        }
    }

    /// <summary>Load again on the next parameters (e.g. a new route value).</summary>
    protected bool Reload { get; set; }
}
