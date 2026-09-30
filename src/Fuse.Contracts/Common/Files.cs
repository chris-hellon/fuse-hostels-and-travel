namespace Fuse.Contracts.Common;

/// <summary>A file sent from the browser (image, video, document).</summary>
public sealed record FileUpload(string FileName, string ContentType, byte[] Content);

/// <summary>A generated file (e.g. an XLSX export) for the browser to save.</summary>
public sealed record FileDownload(string FileName, string ContentType, byte[] Content)
{
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}
