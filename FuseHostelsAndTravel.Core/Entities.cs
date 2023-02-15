namespace FuseHostelsAndTravel.Core
{
    public class BaseEntity
    {
        public int? Id { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public string ShortDescription { get; private set; }
        public string ImageUrl { get; private set; }
        public string FriendlyUrl { get; private set; }
        public string PageTitle { get; private set; }
        public string FriendlyPageTitle { get; private set; }
        public string PageSubTitle { get; private set; }
        public string FriendlyPageSubTitle { get; private set; }

        public BaseEntity()
        {

        }

        public BaseEntity(int? id, string name, string description, string shortDescription, string imageUrl, string pageTitle = null, string pageSubTitle = null)
        {
            Id = id;
            Name = name;
            Description = description;
            ShortDescription = shortDescription;
            ImageUrl = imageUrl;
            FriendlyUrl = name.UrlFriendly();
            PageTitle = pageTitle;
            FriendlyPageTitle = pageTitle.UrlFriendly();
            PageSubTitle = pageSubTitle;
            FriendlyPageSubTitle = pageSubTitle.UrlFriendly();
        }
    }

    public class Hostel : BaseEntity
    {
        public string AddressLine1 { get; private set; }
        public string AddressLine2 { get; private set; }
        public string ContactNumber { get; private set; }
        public string GoogleMapsPlaceId { get; private set; }
        public List<string> Facilities { get; private set; }
        public List<HostelDirection> Directions { get; set; }
        public List<Tour> Tours { get; private set; }
        public List<HostelRoom> Rooms { get; set; }
        public string CloudbedsKey { get; set; }

        public Hostel()
        {

        }

        public Hostel(int? id, string name, string description, string shortDescription, string addressLine1, string addressLine2, string contactNumber, string googleMapsPlaceId, List<string> facilities, string imageUrl, string pageTitle = null, string pageSubTitle = null, List<Tour> tours = null, List<HostelRoom> rooms = null, string cloudbedsKey = null) : base(id, name, description, shortDescription, imageUrl, pageTitle, pageSubTitle)
        {
            AddressLine1 = addressLine1;
            AddressLine2 = addressLine2;
            ContactNumber = contactNumber;
            GoogleMapsPlaceId = googleMapsPlaceId;
            Facilities = facilities;
            Tours = tours;
            Rooms = rooms;
            CloudbedsKey = cloudbedsKey;
        }
    }

    public class HostelDirection
    {
        public int? Id { get; private set; }
        public int HostelId { get; private set; }
        public string Title { get; private set; }
        public string FriendlyTitle { get; private set; }
        public List<HostelDirectionContent> Content { get; private set; }

        public HostelDirection()
        {

        }

        public HostelDirection(int? id, int hostelId, string title, List<HostelDirectionContent> content)
        {
            Id = id;
            HostelId = hostelId;
            Title = title;
            FriendlyTitle = title.UrlFriendly();
            Content = content;
        }
    }

    public class HostelDirectionContent
    {
        public int? Id { get; private set; }
        public int HostelDirectionId { get; private set; }
        public string Body { get; private set; }
        public string Style { get; private set; }

        public HostelDirectionContent()
        {

        }

        public HostelDirectionContent(int? id, int hostelDirectionId, string body, string style = null)
        {
            Id = id;
            HostelDirectionId = hostelDirectionId;
            Body = body;
            Style = style ?? "";
        }
    }

    public class HostelRoom : BaseEntity
    {
        public int HostelId { get; set; }

        public HostelRoom()
        {

        }

        public HostelRoom(int? id, int hostelId, string name, string description, string shortDescription, string imageUrl, string pageTitle = null, string pageSubTitle = null) : base(id, name, description, shortDescription, imageUrl, pageTitle, pageSubTitle)
        {
            HostelId = hostelId;
        }
    }

    public class Tour : BaseEntity
    {
        public List<int> HostelIds { get; private set; }
        public string Duration { get; private set; }
        public string Price { get; private set; }

        public Tour()
        {

        }

        public Tour(int? id, string name, string description, string shortDescription, string imageUrl, string duration, string price, string pageTitle = null, string pageSubTitle = null, List<int> hostelIds = null) : base(id, name, description, shortDescription, imageUrl, pageTitle, pageSubTitle)
        {
            HostelIds = hostelIds;
            Duration = duration;
            Price = price;
        }
    }

    public class Event : BaseEntity
    {
        public string BackgroundColor { get; private set; }

        public Event()
        {

        }

        public Event(int? id, string name, string description, string shortDescription, string imageUrl, string backgroundColor) : base(id, name, description, shortDescription, imageUrl)
        {
            BackgroundColor = backgroundColor;
        }
    }

    public class Image
    {
        public int Id { get; private set; }
        public string ImageSrc { get; private set; }
        public string Title { get; private set; }
        public string SubTitle1 { get; private set; }
        public string SubTitle2 { get; private set; }
        public string Href { get; private set; }

        public Image(int id, string imageSrc, string title, string subTitle1 = null, string subTitle2 = null, string href = null)
        {
            Id = id;
            ImageSrc = imageSrc;
            Title = title;
            SubTitle1 = subTitle1;
            SubTitle2 = subTitle2;
            Href = href;
        }
    }
}

