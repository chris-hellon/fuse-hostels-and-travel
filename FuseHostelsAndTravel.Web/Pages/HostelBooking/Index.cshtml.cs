using Microsoft.AspNetCore.Http.Extensions;

namespace FuseHostelsAndTravel.Web.Pages.HostelBooking
{
	public class IndexModel : BasePageModel
    {
        private ApplicationUser _applicationUser { get; set; } = null;
        public string HostelName { get; set; } = string.Empty;
        public int HostelId { get; set; }
        public string IframeUrl { get; set; }

        [BindProperty]
        public HeaderBannerComponent HeaderBanner { get; private set; }

        public async Task<IActionResult> OnGet(string hostelName, string checkInDate = null, string checkOutDate = null, string userId = null)
        {
            await base.OnGetDataAsync();

            //var location = new Uri($"{Request.Path}{Request.QueryString}");
            var url = Request.GetEncodedUrl();

            base.LoginModal.ReturnUrl = url;
            base.RegisterModal.ReturnUrl = url;

            if (userId != null)
                _applicationUser = await UserManager.FindByIdAsync(userId);

            UserId = userId;

            var hostel = Hostels.Where(h => h.Name.UrlFriendly() == hostelName).FirstOrDefault();

            if (hostel != null)
            {
                var pageTitle = hostel.PageTitle ?? hostel.Name;
                var pageSubTitle = hostel.PageSubTitle ?? "";

                HeaderBanner = new HeaderBannerComponent(pageTitle, pageSubTitle, null, hostel.ImageUrl, new List<OvalContainerComponent>() { new OvalContainerComponent("hostelPageHeaderBannerOvals1", 15, null, -30, null) });
                HostelName = hostel.Name;
                HostelId = hostel.Id.Value;
                IframeUrl = $"https://hotels.cloudbeds.com/reservation/{hostel.CloudbedsKey}";

                if (_applicationUser != null)
                {
                    IframeUrl += "?";

                    if (!string.IsNullOrEmpty(_applicationUser.FirstName))
                        IframeUrl += $"firstName={_applicationUser.FirstName}";

                    if (!string.IsNullOrEmpty(_applicationUser.LastName))
                        IframeUrl += $"&lastName={_applicationUser.LastName}";

                    if (!string.IsNullOrEmpty(_applicationUser.Email))
                        IframeUrl += $"&email={_applicationUser.Email}";

                    if (!string.IsNullOrEmpty(_applicationUser.Nationality))
                        IframeUrl += $"&country={_applicationUser.Nationality}";

                    if (!string.IsNullOrEmpty(_applicationUser.PhoneNumber))
                        IframeUrl += $"&phone={_applicationUser.PhoneNumber}";
                }

                if (!string.IsNullOrEmpty(checkInDate) || !string.IsNullOrEmpty(checkOutDate))
                {
                    IframeUrl += "#";

                    if (!string.IsNullOrEmpty(checkInDate))
                        IframeUrl += $"&checkin={checkInDate}";

                    if (!string.IsNullOrEmpty(checkOutDate))
                        IframeUrl += $"&checkout={checkOutDate}";
                }

                return Page();
            }

            return LocalRedirect("/error");
        }
    }
}
