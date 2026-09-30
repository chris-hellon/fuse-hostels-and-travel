namespace Fuse.Application.Common.Files;

public enum FileKind
{
    Image,
    Video,
}

/// <summary>Uploaded media. Development writes to a local folder; real environments use Azure Blob Storage behind the CDN.</summary>
public interface IFileStore
{
    /// <summary>Stores the file (after <see cref="Uploads.Validate"/>) and returns its public URL.</summary>
    Task<string> SaveAsync(FileUpload file, FileKind kind, CancellationToken cancellationToken);

    /// <summary>
    /// Best effort: a missing file is not an error. Only for rolling back a file this same request just saved;
    /// never for replaced or removed media (legacy never deleted those: files are shared with the site).
    /// </summary>
    Task DeleteAsync(string? url, CancellationToken cancellationToken);

    /// <summary>
    /// True when <paramref name="url"/> is a file of this kind that this store saved for the current
    /// tenant. Guards URLs sent back by the browser (uploaded first, attached later on save).
    /// </summary>
    bool IsTenantFile(string url, FileKind kind);
}

public static class Uploads
{
    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".avif"];
    public static readonly string[] VideoExtensions = [".mp4", ".mov", ".avi", ".wmv", ".webm"];

    /// <summary>Same limits the legacy back office enforced in the browser.</summary>
    public const int MaxImageBytes = 2 * 1024 * 1024;
    public const int MaxVideoBytes = 10 * 1024 * 1024;

    public static void Validate(FileUpload file, FileKind kind, string property = "File")
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var (allowed, max) = kind == FileKind.Image ? (ImageExtensions, MaxImageBytes) : (VideoExtensions, MaxVideoBytes);

        if (!allowed.Contains(extension))
            throw new RequestValidationException(property, $"{file.FileName} isn't a supported {kind.ToString().ToLowerInvariant()} ({string.Join(", ", allowed)}).");
        if (file.Content.Length == 0)
            throw new RequestValidationException(property, $"{file.FileName} is empty.");
        if (file.Content.Length > max)
            throw new RequestValidationException(property, $"{file.FileName} is larger than {max / 1024 / 1024} MB.");
    }
}
