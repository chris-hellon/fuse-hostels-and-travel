namespace Fuse.Contracts.Api;

public static class ApiHeaders
{
    /// <summary>
    /// Required on state-changing <c>/api/app</c> calls. Cross-site forms can't set custom headers,
    /// so together with the SameSite=Lax cookie this blocks CSRF without antiforgery tokens.
    /// </summary>
    public const string Marker = "X-Fuse-Api";

    /// <summary>ProblemDetails extension naming the exception to rethrow on the client.</summary>
    public const string ErrorType = "errorType";

    /// <summary>ProblemDetails extension carrying validation failures (property → messages).</summary>
    public const string Errors = "errors";
}
