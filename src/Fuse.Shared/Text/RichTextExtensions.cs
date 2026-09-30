namespace Fuse.Shared.Text;

public static class RichTextExtensions
{
    /// <summary>Rich-text editors emit an empty paragraph for "no content"; treat it as empty.</summary>
    public static string? NormalizeRichText(this string? value) =>
        value == "<p><br></p>" ? string.Empty : value?.Replace("<p><br></p>", "");
}
