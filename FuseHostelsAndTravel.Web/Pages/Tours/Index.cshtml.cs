using Travaloud.Core.Models.PageModels;
namespace FuseHostelsAndTravel.Web.Pages.Tours
{
    public class IndexModel : TourPageModel
    {
        public override string ToAddress()
        {
            return "contact@fusehostelsandtravel.com";
        }

        public override string[] BccAddress()
        {
            return new string[] { "chris.it@fusehostelsandtravel.com" };
        }

        public override string Subject()
        {
            return "Tour Booking Enquiry";
        }

        public override string LogoImageUrl()
        {
            return "https://scontent.fdad3-1.fna.fbcdn.net/v/t39.30808-6/316660542_132459229600830_1020163277740355105_n.jpg?_nc_cat=108&ccb=1-7&_nc_sid=09cbfe&_nc_ohc=vNs75Ua4grUAX9otYZp&_nc_ht=scontent.fdad3-1.fna&oh=00_AfBg8UDr5MknnKL-aaFl8q2zZxBgW-iKVKEc8LKRZzh0Lw&oe=63FB701F";
        }

        public override string PrimaryBackgroundColor()
        {
            return "#090511";
        }

        public override string SecondaryBackgroundColor()
        {
            return "#f9f9f9";
        }

        public override string HeaderBackgroundColor()
        {
            return "#ab3dff";
        }

        public override string TemplateName()
        {
            return "TourEnquiryTemplate";
        }

        [BindProperty]
        public AlternatingCardHeightComponent ToursCards { get; private set; }

        [BindProperty]
        public Tour Tour { get; private set; }

        [BindProperty]
        public HeaderBannerComponent HeaderBanner { get; private set; }

        public override async Task<IActionResult> OnGetAsync(string tourName = null)
        {
            await base.OnGetDataAsync();

            if (tourName != null)
            {
                Tour = Tours.FirstOrDefault(x => x.FriendlyUrl == tourName);
                if (Tour != null)
                {
                    HeaderBanner = new HeaderBannerComponent(Tour.ImagePath);
                    EnquireNowComponent = new EnquireNowComponent()
                    {
                        TourName = Tour.Name
                    };
                }
            }
            else
                ToursCards = WebComponentsBuilder.FuseHostelsAndTravel.GetToursCards(Tours, "onLoad");

            return Page();
        }
    }
}
