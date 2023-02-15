namespace FuseHostelsAndTravel.Web.Pages.Tours
{
    public class IndexModel : BasePageModel
    {
        [BindProperty]
        public AlternatingCardHeightComponent ToursCards { get; private set; }

        [BindProperty]
        public Tour Tour { get; private set; }

        [BindProperty]
        public HeaderBannerComponent HeaderBanner { get; private set; }

        [BindProperty]
        public EnquireNowInputModel EnquireNowInput { get; set; }

        public class EnquireNowInputModel
        {
            public string TourName { get; set; }

            [Required(ErrorMessage = "Please enter a Name.")]
            public string Name { get; set; }

            [Required(ErrorMessage = "Please enter an Email Address.")]
            [DataType(DataType.EmailAddress)]
            public string Email { get; set; }

            [Display(Name = "Requested Date")]
            [Required(ErrorMessage = "Please select a Date.")]
            public DateTime? Date { get; set; }

            [Display(Name = "Number of People")]
            [Required(ErrorMessage = "Please enter a Number of People.")]
            public int? NumberOfPeople { get; set; }

            [Display(Name = "Any special requests")]
            public string AdditionalInformation { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(string tourName = null)
        {
            await base.OnGetDataAsync();

            if (tourName != null)
            {
                Tour = Tours.FirstOrDefault(x => x.FriendlyUrl == tourName);
                if (Tour != null)
                {
                    HeaderBanner = new HeaderBannerComponent(Tour.ImageUrl);
                    EnquireNowInput = new EnquireNowInputModel()
                    {
                        TourName = Tour.Name
                    };
                }
            }
            else
                ToursCards = WebComponentsBuilder.GetToursCards(Tours, "onLoad");

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            return await OnGetAsync(EnquireNowInput.TourName.UrlFriendly());

            //try
            //{
            //    var emailHtml = await _razorPartialToStringRenderer.RenderPartialToStringAsync("/Pages/EmailTemplates/TourEnquiryTemplate.cshtml", new Pages.EmailTemplates.TourEnquiryTemplateModel(EnquireNowInput.Name, EnquireNowInput.TripName, EnquireNowInput.Email, EnquireNowInput.Date.Value, EnquireNowInput.NumberOfPeople.Value, EnquireNowInput.AdditionalInformation));
            //    await _emailService.Send("info@vietnambackpackerhostels.com", "Tour Booking Enquiry", emailHtml, "Vietnam Backpacker Hostels", "vnbackpackerhostels@gmail.com", new[] { "chrisghellon@gmail.com" });
            //    ViewData["SuccessMessage"] = "<p>Your request has been sent successfully.</p><p>A member of our team will contact you shortly.</p>";
            //    ModelState.Clear();
            //}
            //catch (Exception e)
            //{
            //    ErrorLoggerService.LogError(e);
            //    ViewData["ErrorMessage"] = "<p>There was an error submitting your enquiry.</p><p>Please try again, or contact us at info@vietnambackpackerhostels.com.</p>";
            //}

            //return await OnGet(EnquireNowInput.Category.UrlFriendly(), EnquireNowInput.TripName.UrlFriendly());
        }
    }
}
