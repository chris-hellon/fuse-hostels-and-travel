using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using Fuse.Application.Common.Files;
using Fuse.Application.Common.Persistence;
using Fuse.Contracts.Common;

namespace Fuse.Infrastructure.Files;

/// <summary>
/// Legacy's "AzureStorageSettings" section. Its connection string's BlobEndpoint is the CDN
/// (https://travaloud.azureedge.net), which is also the public base of every file URL. Without one, files go to the
/// app's local .uploads folder (served at /uploads), as in tests.
/// </summary>
public sealed class FileStorageOptions
{
    public const string Section = "AzureStorageSettings";

    public string? ConnectionString { get; set; }

    public bool UsesAzure => !string.IsNullOrWhiteSpace(ConnectionString);
}

internal static class FilePaths
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>assets/{images|videos}/{guid}{ext}, inside the tenant's container: the legacy URL shape
    /// ({cdn}/{tenant}/assets/images/...).</summary>
    public static string For(FileUpload file, FileKind kind) =>
        $"assets/{(kind == FileKind.Image ? "images" : "videos")}/{Guid.NewGuid():N}{Path.GetExtension(file.FileName).ToLowerInvariant()}";

    /// <summary>{root}assets/{images|videos}/{name}{ext}: one generated file name, nothing else (no "..", no sub-folders).</summary>
    public static bool IsFileUnder(string url, string root, FileKind kind)
    {
        var bare = Domain.Common.ImageUrls.Unsized(url) ?? "";
        var prefix = root + (kind == FileKind.Image ? "assets/images/" : "assets/videos/");
        if (!bare.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        var name = bare[prefix.Length..];
        return name.Length > 0 && !name.Contains('/') && !name.Contains('\\') && !name.Contains("..")
               && (kind == FileKind.Image ? Uploads.ImageExtensions : Uploads.VideoExtensions).Contains(Path.GetExtension(name).ToLowerInvariant());
    }

    public static string ContentType(string path) =>
        ContentTypes.TryGetContentType(path, out var type) ? type : "application/octet-stream";
}

/// <summary>Development: writes under {ContentRoot}/.uploads and serves them at /uploads.</summary>
public sealed class LocalFileStore(IWebHostEnvironment environment, ITenantContext tenant) : IFileStore
{
    public const string RequestPath = "/uploads";

    public static string Root(IWebHostEnvironment environment) => Path.Combine(environment.ContentRootPath, ".uploads");

    public async Task<string> SaveAsync(FileUpload file, FileKind kind, CancellationToken cancellationToken)
    {
        Uploads.Validate(file, kind);
        var relative = $"{tenant.TenantId}/{FilePaths.For(file, kind)}";
        var path = Path.Combine(Root(environment), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, file.Content, cancellationToken);
        return $"{RequestPath}/{relative}";
    }

    public bool IsTenantFile(string url, FileKind kind) =>
        FilePaths.IsFileUnder(url, $"{RequestPath}/{tenant.TenantId}/", kind);

    public Task DeleteAsync(string? url, CancellationToken cancellationToken)
    {
        var bare = Domain.Common.ImageUrls.Unsized(url);
        if (bare?.StartsWith(RequestPath + "/", StringComparison.Ordinal) == true)
        {
            var path = Path.GetFullPath(Path.Combine(Root(environment), bare[(RequestPath.Length + 1)..]));
            if (path.StartsWith(Root(environment), StringComparison.Ordinal) && File.Exists(path))
                File.Delete(path);
        }
        return Task.CompletedTask;
    }
}

/// <summary>Real environments: Azure Blob Storage (one container per tenant), returned via the CDN host.</summary>
internal sealed class AzureBlobFileStore(IOptions<FileStorageOptions> options, ITenantContext tenant) : IFileStore
{
    public async Task<string> SaveAsync(FileUpload file, FileKind kind, CancellationToken cancellationToken)
    {
        Uploads.Validate(file, kind);
        var path = FilePaths.For(file, kind);
        var blob = Container(tenant.TenantId).GetBlobClient(path);
        using var stream = new MemoryStream(file.Content, writable: false);
        await blob.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = FilePaths.ContentType(path), CacheControl = "max-age=31536000" },
        }, cancellationToken);
        return $"{blob.Uri.GetLeftPart(UriPartial.Authority)}/{tenant.TenantId}/{path}";
    }

    public bool IsTenantFile(string url, FileKind kind) =>
        FilePaths.IsFileUnder(url, $"{Container(tenant.TenantId).Uri.GetLeftPart(UriPartial.Authority)}/{tenant.TenantId}/", kind);

    public async Task DeleteAsync(string? url, CancellationToken cancellationToken)
    {
        if (Domain.Common.ImageUrls.Unsized(url) is not { Length: > 0 } bare || !Uri.TryCreate(bare, UriKind.Absolute, out var uri))
            return;
        var segments = uri.AbsolutePath.TrimStart('/').Split('/', 2);
        if (segments.Length == 2)
            await Container(segments[0]).GetBlobClient(segments[1]).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    private BlobContainerClient Container(string name) =>
        new(options.Value.ConnectionString ?? throw new InvalidOperationException("AzureStorageSettings:ConnectionString is not configured."), name);
}
