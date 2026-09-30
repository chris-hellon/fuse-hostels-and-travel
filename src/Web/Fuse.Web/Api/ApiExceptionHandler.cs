using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Fuse.Contracts.Api;
using Fuse.Contracts.Common;

namespace Fuse.Web.Api;

/// <summary>
/// Turns exceptions from <c>/api</c> into ProblemDetails. <see cref="AppException"/>s carry an
/// <c>errorType</c> so <c>HttpApi</c> can rethrow the same type in the browser; anything else is a
/// logged 500 with no internals leaked.
/// </summary>
internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        // By now the middleware has rewritten Request.Path to the error page; use the original.
        var path = context.Features.Get<IExceptionHandlerPathFeature>()?.Path ?? context.Request.Path;
        if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
            return false;

        var problem = exception switch
        {
            AppException app => new ProblemDetails { Status = app.StatusCode, Title = app.Message },
            BadHttpRequestException bad => new ProblemDetails { Status = 400, Title = bad.Message },
            System.Text.Json.JsonException => new ProblemDetails { Status = 400, Title = "The request body is not valid JSON for this request." },
            _ => new ProblemDetails { Status = 500, Title = new ServerErrorException().Message },
        };

        if (exception is AppException appException)
        {
            problem.Extensions[ApiHeaders.ErrorType] = appException.GetType().Name;
            if (appException is RequestValidationException validation)
                problem.Extensions[ApiHeaders.Errors] = validation.Errors;
        }
        else if (problem.Status == 500)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", context.Request.Method, path);
        }

        // Written directly (not via IProblemDetailsService) so the body never depends on the
        // caller's Accept header: HttpApi always needs it to rethrow the right exception.
        context.Response.StatusCode = problem.Status!.Value;
        await context.Response.WriteAsJsonAsync(problem, ApiJson.Options, "application/problem+json", cancellationToken);
        return true;
    }
}
