using Fuse.Contracts.Auth;

namespace Fuse.UI.Auth;

/// <summary>
/// Sign-in, registration and sign-out for components. Runs only in the browser (it must receive
/// the auth cookie on a real HTTP response); on success the app's authentication state updates
/// in place, with no page reload.
/// </summary>
public interface IAuthClient
{
    Task SignInAsync(SignInRequest request, CancellationToken cancellationToken = default);

    Task RegisterAsync(RegisterGuestRequest request, CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);
}
