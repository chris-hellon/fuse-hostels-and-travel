using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Fuse.Admin.Components.Forms;

/// <summary>
/// One section shown by <see cref="ContentSections"/>: a rich text field (e.g. a tour's "What's included"),
/// or, made with <see cref="Fields"/>, a few ordinary fields (e.g. "Details": title and sub title).
/// </summary>
public sealed partial record ContentSection(string Label, string? Hint, Func<string?> Get, Action<string?> Set, bool Required = false)
{
    /// <summary>Ordinary fields shown instead of the editor (see <see cref="Fields"/>).</summary>
    public RenderFragment? Form { get; init; }

    /// <summary>Whether a <see cref="Form"/> section is filled in (drives its marker and required check).</summary>
    public Func<bool>? Filled { get; init; }

    public bool HasText => Filled?.Invoke() ?? HasVisibleText(Get());

    /// <summary>A section of ordinary fields, listed with the text sections (e.g. a guide's title and sub title).</summary>
    public static ContentSection Fields(string label, string? hint, RenderFragment form, Func<bool> filled, bool required = false) =>
        new(label, hint, () => null, _ => { }, required) { Form = form, Filled = filled };

    /// <summary>True when the HTML has any text (Quill leaves empty paragraphs behind).</summary>
    public static bool HasVisibleText(string? html) =>
        !string.IsNullOrWhiteSpace(WebUtility.HtmlDecode(Tags().Replace(html ?? "", " ")));

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();
}
