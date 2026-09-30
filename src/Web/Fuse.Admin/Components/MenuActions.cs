namespace Fuse.Admin.Components;

/// <summary>
/// A MudBlazor menu closes only once its item's OnClick finishes, so an item that opens a dialog and
/// awaits it leaves the menu open behind the dialog. Wrap such actions: the click returns at once and
/// the action runs on the next turn; a failure goes to <paramref name="onError"/> (the component's
/// DispatchExceptionAsync, so it surfaces like any other render error).
/// </summary>
public static class MenuActions
{
    public static Task AfterMenuCloses(Func<Task> action, Func<Exception, Task> onError)
    {
        _ = RunAsync();
        return Task.CompletedTask;

        async Task RunAsync()
        {
            await Task.Yield();
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                await onError(ex);
            }
        }
    }
}
