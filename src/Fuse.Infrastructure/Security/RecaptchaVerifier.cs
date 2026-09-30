using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Fuse.Application.Common.Security;

namespace Fuse.Infrastructure.Security;

/// <summary>Google's siteverify. Fails closed: no secret configured, or Google unreachable, means not verified.</summary>
internal sealed class RecaptchaVerifier(HttpClient http, IOptions<CaptchaOptions> options, ILogger<RecaptchaVerifier> logger) : ICaptcha
{
    public async Task<bool> VerifyAsync(string response, CancellationToken cancellationToken)
    {
        var secret = options.Value.SecretKey;
        if (string.IsNullOrWhiteSpace(secret))
        {
            logger.LogError("reCAPTCHA isn't configured (ReCaptcha:SecretKey), so no enquiry can be sent");
            return false;
        }
        try
        {
            using var reply = await http.PostAsync("https://www.google.com/recaptcha/api/siteverify",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["secret"] = secret, ["response"] = response }), cancellationToken);
            var result = await reply.Content.ReadFromJsonAsync<Result>(cancellationToken);
            if (result?.Success != true)
                logger.LogInformation("reCAPTCHA refused: {Errors}", string.Join(", ", result?.Errors ?? []));
            return result?.Success == true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(e, "reCAPTCHA couldn't be checked");
            return false;
        }
    }

    private sealed record Result(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error-codes")] string[]? Errors);
}
