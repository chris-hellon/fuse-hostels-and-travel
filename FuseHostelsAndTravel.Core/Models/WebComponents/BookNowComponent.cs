namespace FuseHostelsAndTravel.Core.Models.WebComponents
{
	public class BookNowComponent : BaseComponent
    {
        public List<Hostel> Hostels { get; private set; }

        [Required(ErrorMessage = "Please select a Hostel")]
        public int? HostelId { get; private set; }

        [Required(ErrorMessage = "Please choose a Check In Date")]
        public DateTime? CheckInDate { get; private set; }

        [Required(ErrorMessage = "Please choose a Check In Date")]
        public DateTime? CheckOutDate { get; private set; }

        public int? GuestQuantity { get; private set; }

        public bool Floating { get; private set; }

        public bool IsModal { get; set; }

        public BookNowComponent()
        {

        }

        public BookNowComponent(List<Hostel> hostels, int? hostelId = null, bool floating = false)
        {
            Hostels = hostels;
            HostelId = hostelId;
            Floating = floating;
        }

        public BookNowComponent(int? hostelId, DateTime? checkInDate, DateTime? checkOutDate, bool floating = false, int? guestQuantity = null)
        {
            HostelId = hostelId;
            CheckInDate = checkInDate;
            CheckOutDate = checkOutDate;
            GuestQuantity = guestQuantity;
            Floating = floating;
        }

        public BookNowComponent(bool floating = false)
        {
            Floating = floating;
        }
    }
}

