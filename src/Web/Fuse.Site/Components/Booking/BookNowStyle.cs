namespace Fuse.Site.Components.Booking;

/// <summary>Where the booking form sits (legacy BookNowComponent Floating / IsModal).</summary>
public enum BookNowStyle
{
    /// <summary>Floating over the home page hero: a light box, fields in a row, "Book Now".</summary>
    Hero,
    /// <summary>In a white banner (e.g. the hostels page): fields in a row, "Search For Rooms".</summary>
    Banner,
    /// <summary>In the Book modal: fields stacked, "Book Now".</summary>
    Modal,
}
