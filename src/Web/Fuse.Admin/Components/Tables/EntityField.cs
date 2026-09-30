using Microsoft.AspNetCore.Components;

namespace Fuse.Admin.Components.Tables;

/// <summary>A column in an <see cref="EntityTable{T}"/> (same shape as the legacy EntityField).</summary>
/// <param name="Value">Cell value; bools render as a read-only checkbox, others as highlighted text.</param>
/// <param name="Title">Header text.</param>
/// <param name="SortLabel">Sort key sent to the server (the row property name); empty = not sortable.</param>
/// <param name="Template">Custom cell content instead of the value.</param>
public sealed record EntityField<T>(Func<T, object?> Value, string Title, string SortLabel = "", RenderFragment<T>? Template = null)
{
    public bool IsBool { get; init; }

    /// <summary>A column width; "1%" shrinks a short column (e.g. a reference) to fit its content.</summary>
    public string? Width { get; init; }

    public static EntityField<T> Bool(Func<T, bool> value, string title, string sortLabel = "") =>
        new(x => value(x), title, sortLabel) { IsBool = true };

    /// <summary>A booking reference (or similar short number): as narrow as its content, never the wide first column
    /// names get.</summary>
    public static EntityField<T> Reference(Func<T, object?> value, string sortLabel, string title = "Reference", RenderFragment<T>? template = null) =>
        new(value, title, sortLabel, template) { Width = "1%" };
}
