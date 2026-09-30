using FluentValidation;

namespace Fuse.UI.Forms;

/// <summary>
/// Runs a contract's FluentValidation rules for one field, in the shape MudBlazor inputs take
/// (<c>Validation="@Rules.For(...)"</c>, or <c>MudForm.Validation</c>). The same validators run
/// again on the server, so this is purely for instant feedback.
/// </summary>
public sealed class FluentRules<T>(IValidator<T> validator)
{
    public Func<object, string, Task<IEnumerable<string>>> Validate => async (model, propertyName) =>
    {
        var result = await validator.ValidateAsync(ValidationContext<T>.CreateWithOptions((T)model, o => o.IncludeProperties(propertyName)));
        return result.IsValid ? [] : result.Errors.Select(e => e.ErrorMessage);
    };
}

public static class ServerErrors
{
    /// <summary>Flattens a server validation failure for an alert.</summary>
    public static IReadOnlyList<string> Messages(Contracts.Common.RequestValidationException exception) =>
        [.. exception.Errors.SelectMany(e => e.Value).Distinct()];
}
