using MudBlazor;

namespace Fuse.Admin.Components.Dialogs;

public static class ConfirmExtensions
{
    /// <summary>Asks a yes/no question with <see cref="ConfirmDialog"/>; true when the user confirms.</summary>
    public static async Task<bool> ConfirmAsync(this IDialogService dialogs, string title, string message, string confirmText,
        string cancelText = "Cancel", string? detail = null, bool destructive = true)
    {
        var dialog = await dialogs.ShowAsync<ConfirmDialog>(null, new DialogParameters<ConfirmDialog>
        {
            { d => d.Title, title },
            { d => d.Message, message },
            { d => d.Detail, detail },
            { d => d.ConfirmText, confirmText },
            { d => d.CancelText, cancelText },
            { d => d.Destructive, destructive },
        }, new DialogOptions { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true, CloseButton = false });
        return await dialog.Result is { Canceled: false };
    }
}
