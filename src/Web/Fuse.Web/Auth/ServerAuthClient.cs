using Fuse.Contracts.Auth;
using Fuse.UI.Auth;

namespace Fuse.Web.Auth;

/// <summary>
/// Components get an <see cref="IAuthClient"/> during prerendering too, but nothing can click
/// "Sign in" until the page is interactive (in the browser), so these never run.
/// </summary>
internal sealed class ServerAuthClient : IAuthClient
{
    public Task SignInAsync(SignInRequest request, CancellationToken cancellationToken = default) => throw InteractiveOnly();
    public Task RegisterAsync(RegisterGuestRequest request, CancellationToken cancellationToken = default) => throw InteractiveOnly();
    public Task SignOutAsync(CancellationToken cancellationToken = default) => throw InteractiveOnly();

    private static InvalidOperationException InteractiveOnly() => new("Authentication runs in the browser.");
}
