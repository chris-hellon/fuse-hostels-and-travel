namespace Fuse.Contracts.Common;

/// <summary>
/// Expected failures. Thrown by handlers, turned into ProblemDetails by the host, and rethrown
/// as the same type by <c>HttpApi</c>, so UI code handles them identically in every render mode.
/// </summary>
public abstract class AppException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class NotFoundException(string message = "The requested item could not be found.") : AppException(message, 404);

public sealed class ConflictException(string message) : AppException(message, 409);

/// <summary>A business rule rejected the request (for example deleting a tour with future bookings).</summary>
public sealed class BusinessRuleException(string message) : AppException(message, 422);

public sealed class UnauthorizedException(string message = "You need to sign in to do that.") : AppException(message, 401);

public sealed class ForbiddenException(string message = "You don't have permission to do that.") : AppException(message, 403);

public sealed class RequestValidationException : AppException
{
    public RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.", 400) => Errors = errors;

    public RequestValidationException(string property, string message)
        : this(new Dictionary<string, string[]> { [property] = [message] }) { }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>Maps the <c>errorType</c> ProblemDetails extension back to an exception.</summary>
public static class AppExceptions
{
    public static AppException Create(string? errorType, string message, IReadOnlyDictionary<string, string[]>? errors, int statusCode) =>
        errorType switch
        {
            nameof(NotFoundException) => new NotFoundException(message),
            nameof(ConflictException) => new ConflictException(message),
            nameof(BusinessRuleException) => new BusinessRuleException(message),
            nameof(UnauthorizedException) => new UnauthorizedException(message),
            nameof(ForbiddenException) => new ForbiddenException(message),
            nameof(RequestValidationException) => new RequestValidationException(errors ?? new Dictionary<string, string[]>()),
            _ => statusCode switch
            {
                401 => new UnauthorizedException(),
                403 => new ForbiddenException(),
                404 => new NotFoundException(),
                _ => new ServerErrorException(message),
            },
        };
}

/// <summary>An unexpected server failure, surfaced on the client. Details stay in the server log.</summary>
public sealed class ServerErrorException(string message = "Something went wrong. Please try again.") : AppException(message, 500);
