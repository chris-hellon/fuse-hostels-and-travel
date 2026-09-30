namespace Fuse.Site.Media;

/// <summary>Image URLs at the width a slot needs (legacy <c>FormatImageUrl</c>).</summary>
public static class CdnImages
{
    private const string Blob = "https://travaloud.azureedge.net/";
    private const string ResizingCdn = "https://travaloudcdn.azureedge.net/";
    /// <summary>Images from before the move to Azure, still referenced by some records (legacy mapped them the same way).</summary>
    private const string ImageKit = "https://ik.imagekit.io/rqlzhe7ko/";

    /// <summary>
    /// Blob (and old ImageKit) images are served through the resizing CDN with a <c>?w=</c> width. Other URLs (development
    /// uploads, stock photos) are returned as they are.
    /// </summary>
    public static string? Sized(string? url, int width)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;
        var bare = url.Split('?')[0];
        if (bare.StartsWith(Blob, StringComparison.OrdinalIgnoreCase))
            bare = ResizingCdn + bare[Blob.Length..];
        else if (bare.StartsWith(ImageKit, StringComparison.OrdinalIgnoreCase))
            bare = $"{ResizingCdn}fuse/assets/images/{bare[ImageKit.Length..]}";
        return bare.StartsWith(ResizingCdn, StringComparison.OrdinalIgnoreCase) ? $"{bare}?w={width}" : url;
    }

    /// <summary>Width of the preview shown while a photo loads: a couple of KB, scaled up (so softened) by the browser.</summary>
    private const int PreviewWidth = 64;

    /// <summary>
    /// A CSS <c>background-image</c> for a photo: the photo at <paramref name="width"/> over a tiny preview of itself,
    /// which arrives almost at once, so the space shows a soft version of the photo instead of sitting empty.
    /// Photos the CDN can't resize get no preview.
    /// </summary>
    public static string Background(string? url, int width)
    {
        var full = Sized(url, width);
        var preview = Sized(url, PreviewWidth);
        return preview == full ? $"url('{full}')" : $"url('{full}'), url('{preview}')";
    }
}
