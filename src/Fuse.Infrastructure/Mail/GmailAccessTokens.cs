using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.Extensions.Options;

namespace Fuse.Infrastructure.Mail;

/// <summary>
/// Gmail access tokens for XOAUTH2 (legacy GmailOAuthTokenProvider). A singleton: the one Google credential, built from the
/// refresh token, caches its access token and refreshes it when it expires, so it isn't minted per email.
/// </summary>
internal sealed class GmailAccessTokens(IOptions<MailOptions> options)
{
    private const string MailScope = "https://mail.google.com/";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private UserCredential? _credential;

    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        var credential = await CredentialAsync(cancellationToken);
        return await credential.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
    }

    private async Task<UserCredential> CredentialAsync(CancellationToken cancellationToken)
    {
        if (_credential is not null)
            return _credential;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_credential is not null)
                return _credential;
            var settings = options.Value.GmailOAuth;
            if (!settings.IsConfigured)
                throw new InvalidOperationException("Mail:GmailOAuth isn't configured (it needs ClientId, ClientSecret, RefreshToken and AccountEmail).");

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets { ClientId = settings.ClientId, ClientSecret = settings.ClientSecret },
                Scopes = [MailScope],
            });
            return _credential = new UserCredential(flow, settings.AccountEmail, new TokenResponse { RefreshToken = settings.RefreshToken });
        }
        finally
        {
            _gate.Release();
        }
    }
}
