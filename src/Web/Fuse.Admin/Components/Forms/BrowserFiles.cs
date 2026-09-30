using Microsoft.AspNetCore.Components.Forms;

namespace Fuse.Admin.Components.Forms;

public static class BrowserFiles
{
    /// <summary>Reads a picked file into an upload contract. The server re-checks type and size.</summary>
    public static async Task<FileUpload> ToUploadAsync(this IBrowserFile file, long maxBytes = 10 * 1024 * 1024)
    {
        using var buffer = new MemoryStream();
        await using var stream = file.OpenReadStream(maxBytes);
        await stream.CopyToAsync(buffer);
        return new FileUpload(file.Name, file.ContentType, buffer.ToArray());
    }
}
