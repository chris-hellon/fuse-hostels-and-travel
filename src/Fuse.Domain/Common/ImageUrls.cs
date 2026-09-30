namespace Fuse.Domain.Common;

/// <summary>
/// Stored image URLs carry a width hint the CDN resizes on (<c>?w=1000</c> full size,
/// <c>?w=700</c> thumbnails). Replaces the legacy per-entity checks, some of which were inverted.
/// </summary>
public static class ImageUrls
{
    public const int Full = 1000;
    public const int Thumbnail = 700;

    public static string? Sized(string? url, int width)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;
        var bare = url.Split('?')[0];
        return $"{bare}?w={width}";
    }

    public static string? Unsized(string? url) => string.IsNullOrWhiteSpace(url) ? url : url.Split('?')[0];
}
