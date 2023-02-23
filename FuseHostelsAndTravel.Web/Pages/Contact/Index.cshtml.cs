using Travaloud.Core.Models.PageModels;

namespace FuseHostelsAndTravel.Web.Pages.Contact
{
    public class IndexModel : ContactPageModel
    {
        public override string ToAddress()
        {
            return "contact.it@fusehostelsandtravel.com";
        }

        public override string[] BccAddress()
        {
            return new string[] { "chris.it@fusehostelsandtravel.com" };
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

        public IEnumerable<OvalContainerComponent> OvalContainers { get; private set; } = null;


        public override async Task<IActionResult> OnGetAsync(string tourName = null)
        {
            await base.OnGetDataAsync();

            OvalContainers = new List<OvalContainerComponent>()
                {
                    new OvalContainerComponent("contactPageOvals1", -20, null, -18, null),
                    new OvalContainerComponent("contactPageOvals2", null, -20, null, -18)
                };

            return Page();
        }
    }
}
