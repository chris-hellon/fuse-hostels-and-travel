using MudBlazor;

namespace Fuse.Admin.Components;

public static class SnackbarExtensions
{
    /// <summary>
    /// Runs an API call, shows <paramref name="success"/> or the failure, and reports whether it
    /// worked. Validation failures list every message.
    /// </summary>
    public static async Task<bool> TryAsync(this ISnackbar snackbar, Func<Task> action, string? success = null)
    {
        try
        {
            await action();
            if (success is not null)
                snackbar.Add(success, Severity.Success);
            return true;
        }
        catch (RequestValidationException validation)
        {
            foreach (var message in validation.Errors.SelectMany(e => e.Value).Distinct())
                snackbar.Add(message, Severity.Error);
            return false;
        }
        catch (AppException failure)
        {
            snackbar.Add(failure.Message, Severity.Error);
            return false;
        }
    }
}
