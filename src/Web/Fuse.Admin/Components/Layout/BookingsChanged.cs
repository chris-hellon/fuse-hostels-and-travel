namespace Fuse.Admin.Components.Layout;

/// <summary>
/// Raised when a booking is made outside the page showing bookings (the app bar's New booking), so that page can
/// reload its list. The admin layout cascades one to every page.
/// </summary>
public sealed class BookingsChanged
{
    public event Func<Task>? Changed;

    public Task NotifyAsync() => Changed?.Invoke() ?? Task.CompletedTask;
}
