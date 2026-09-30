namespace Fuse.Site.Layout;

/// <summary>
/// The site-wide dialogs, which live in the layout (so they sit above everything and survive page
/// changes). The layout cascades itself as this.
/// </summary>
public interface ISiteDialogs
{
    void OpenSignIn();

    void OpenBookNow();

    /// <summary>Enquire Now for a tour that can't be booked online.</summary>
    void OpenTourEnquiry(DefaultIdType tourId, string tourName);

    /// <summary>Opens the check in / out picker; <paramref name="picked"/> gets the range.</summary>
    void PickDates(DateOnly? start, DateOnly? end, Func<DateOnly, DateOnly, Task> picked);
}
