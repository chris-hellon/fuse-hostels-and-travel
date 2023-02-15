namespace FuseHostelsAndTravel.Web.Pages.Hostels
{
	public class IndexModel : BasePageModel
    {
        [BindProperty]
        public Hostel Hostel { get; private set; }

        [BindProperty]
        public ContainerHalfImageRoundedTextComponent IntroductionBanner { get; private set; }

        [BindProperty]
        public HeaderBannerComponent HeaderBanner { get; private set; }

        [BindProperty]
        public List<ContainerHalfImageTextComponent> HostelsContainers { get; private set; } = new List<ContainerHalfImageTextComponent>();

        [BindProperty]
        public CarouselCardsComponent ToursCards { get; private set; }

        [BindProperty]
        public FullImageCardComponent AccommodationCards { get; private set; }

        [BindProperty]
        public CarouselCardsComponent EventsCards { get; private set; }

        [BindProperty]
        public List<NavPill> NavPills { get; private set; }

        [BindProperty]
        public NavPillsComponent DirectionsNavPills { get; private set; }

        [BindProperty]
        public BookNowComponent BookNowBanner { get; private set; }

        [BindProperty]
        public FeaturesTableComponent FacilitiesTable { get; private set; }

        public async Task<IActionResult> OnGet(string hostelName = null)
        {
            await base.OnGetDataAsync();

            EventsCards = WebComponentsBuilder.GetEventsCarouselCards(Events);

            if (hostelName == null)
            {
                HeaderBanner = new HeaderBannerComponent("HOSTELS", "VIETNAM", "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam.", "https://images.unsplash.com/photo-1466378284817-a6b7fd50cc68?ixlib=rb-4.0.3&ixid=MnwxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8&auto=format&fit=crop&w=2880&q=80", new List<OvalContainerComponent>()
                       {
                             new OvalContainerComponent("hostelPageHeaderBannerOvals1", -160, null, -45, null)
                       });
                HostelsContainers = WebComponentsBuilder.GetHostelsContainers(Hostels);
                BookNowBanner = new BookNowComponent(Hostels);
            }
            else
            {
                Hostel = Hostels.FirstOrDefault(x => x.FriendlyUrl == hostelName);
                if (Hostel != null)
                {
                    Hostel.Rooms = await MockData.GetHostelRooms(Hostel.Id.Value);
                    Hostel.Directions = await MockData.GetHostelDirections(Hostel.Id.Value);

                    var pageTitle = Hostel.PageTitle ?? Hostel.Name;
                    var pageSubTitle = Hostel.PageSubTitle ?? "";

                    HeaderBanner = new HeaderBannerComponent(pageTitle, pageSubTitle, null, Hostel.ImageUrl, new List<OvalContainerComponent>()
                       {
                             new OvalContainerComponent("hostelPageHeaderBannerOvals1", 15, null, -30, null)
                       });

                    DirectionsNavPills = WebComponentsBuilder.GetHostelDirectionsNavPills(Hostel);
                    IntroductionBanner = new ContainerHalfImageRoundedTextComponent(new List<string>() { "ABOUT" }, null, Hostel.Description,
                       "https://images.unsplash.com/photo-1555979864-7a8f9b4fddf8?ixlib=rb-4.0.3&ixid=MnwxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8&auto=format&fit=crop&w=3542&q=80", null, new List<OvalContainerComponent>()
                       {
                             new OvalContainerComponent("hostelPageIntroductionOvals1", 15, null, null, -28),
                             new OvalContainerComponent("hostelPageIntroductionOvals2", null, 15, null, 18)
                       });
                    ToursCards = WebComponentsBuilder.GetToursCarouselCards(Tours, "onScroll", $"TOURS IN {pageTitle.ToUpper()}", null);
                    AccommodationCards = WebComponentsBuilder.GetHostelAccommodationCards(Hostel.Rooms, "onScroll");
                    
                    FacilitiesTable = WebComponentsBuilder.GetHostelFacilities(Hostel);

                    NavPills = new List<NavPill>()
                    {
                        new NavPill("ABOUT", 1400),
                        new NavPill("ACCOMMODATION", 1600),
                        new NavPill($"TOURS IN {pageTitle.ToUpper()}", 1800),
                        new NavPill($"GETTING TO {pageTitle.ToUpper()}", 2000),
                    };
                }

                BookNowBanner = new BookNowComponent(Hostels, Hostel.Id);
            }

            return Page();
        }
    }
}
