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

    //public class Event : BaseEntity
    //{
    //    public string BackgroundColor { get; private set; }

    //    public Event()
    //    {

    //    }

    //    public Event(int? id, string name, string description, string shortDescription, string imageUrl, string backgroundColor) : base(id, name, description, shortDescription, imageUrl)
    //    {
    //        BackgroundColor = backgroundColor;
    //    }
    //}

    //public class Image
    //{
    //    public int Id { get; private set; }
    //    public string ImageSrc { get; private set; }
    //    public string Title { get; private set; }
    //    public string SubTitle1 { get; private set; }
    //    public string SubTitle2 { get; private set; }
    //    public string Href { get; private set; }

    //    public Image(int id, string imageSrc, string title, string subTitle1 = null, string subTitle2 = null, string href = null)
    //    {
    //        Id = id;
    //        ImageSrc = imageSrc;
    //        Title = title;
    //        SubTitle1 = subTitle1;
    //        SubTitle2 = subTitle2;
    //        Href = href;
    //    }
    //}
}

