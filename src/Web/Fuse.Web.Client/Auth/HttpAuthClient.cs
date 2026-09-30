using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fuse.Contracts.Api;
using Fuse.Contracts.Auth;
using Fuse.Contracts.Common;
using Fuse.UI.Auth;

namespace Fuse.Web.Client.Auth;

internal sealed class HttpAuthClient(HttpClient http, ClientAuthenticationStateProvider authState) : IAuthClient
{
    public Task SignInAsync(SignInRequest request, CancellationToken cancellationToken = default) =>
        PostAsync(AuthRoutes.SignIn, request, cancellationToken);

    public Task RegisterAsync(RegisterGuestRequest request, CancellationToken cancellationToken = default) =>
        PostAsync(AuthRoutes.Register, request, cancellationToken);

    public Task SignOutAsync(CancellationToken cancellationToken = default) =>
        PostAsync<object?>(AuthRoutes.SignOut, null, cancellationToken);

    private async Task PostAsync<T>(string route, T body, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(body, options: ApiJson.Options),
        };
        message.Headers.Add(ApiHeaders.Marker, "1");

        using var response = await http.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonObject>(ApiJson.Options, cancellationToken);
            throw AppExceptions.Create(
                problem?[ApiHeaders.ErrorType]?.GetValue<string>(),
                problem?["title"]?.GetValue<string>() ?? "Something went wrong. Please try again.",
                problem?[ApiHeaders.Errors]?.Deserialize<Dictionary<string, string[]>>(ApiJson.Options),
                (int)response.StatusCode);
        }

        // Re-read rather than trusting the response: /me includes the permission claims the
        // server adds per request.
        await authState.RefreshAsync(cancellationToken);
    }
}
