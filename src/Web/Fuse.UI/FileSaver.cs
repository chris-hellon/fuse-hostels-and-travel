using Microsoft.JSInterop;
using Fuse.Contracts.Common;

namespace Fuse.UI;

public sealed class FileSaver(IJSRuntime js)
{
    public async Task SaveAsync(FileDownload file)
    {
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/Fuse.UI/files.js");
        await module.InvokeVoidAsync("saveFile", file.FileName, file.ContentType, Convert.ToBase64String(file.Content));
    }
}
