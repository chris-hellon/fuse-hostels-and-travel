namespace FuseHostelsAndTravel.Web.Pages.Hostels
{
	public class IndexModel : TravaloudBasePageModel
    {
        [BindProperty]
        public Travaloud.Core.Entities.Catalog.Property Property { get; private set; }

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

        public async Task<IActionResult> OnGet(string propertyName = null)
        {
            await base.OnGetDataAsync();

            EventsCards = WebComponentsBuilder.FuseHostelsAndTravel.GetEventsCarouselCards(Events);

            if (propertyName == null)
            {
                HeaderBanner = new HeaderBannerComponent("HOSTELS", "VIETNAM", "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam.", "https://images.unsplash.com/photo-1466378284817-a6b7fd50cc68?ixlib=rb-4.0.3&ixid=MnwxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8&auto=format&fit=crop&w=2880&q=80", new List<OvalContainerComponent>()
                       {
                             new OvalContainerComponent("hostelPageHeaderBannerOvals1", -160, null, -45, null)
                       });
                HostelsContainers = WebComponentsBuilder.FuseHostelsAndTravel.GetHostelsContainers(Properties);
                BookNowBanner = new BookNowComponent(Properties);
            }
            else
            {
                Property = Properties.FirstOrDefault(x => x.FriendlyUrl == propertyName);
                if (Property != null)
                {
                    await PropertiesRepository.GetPropertyInformation(Property);

                    var pageTitle = Property.PageTitle ?? Property.Name;
                    var pageSubTitle = Property.PageSubTitle ?? "";

                    HeaderBanner = new HeaderBannerComponent(pageTitle, pageSubTitle, null, Property.ImagePath, new List<OvalContainerComponent>()
                       {
                             new OvalContainerComponent("hostelPageHeaderBannerOvals1", 15, null, -30, null)
                       });

                    DirectionsNavPills = WebComponentsBuilder.FuseHostelsAndTravel.GetHostelDirectionsNavPills(Property);
                    IntroductionBanner = new ContainerHalfImageRoundedTextComponent(new List<string>() { "ABOUT" }, null, Property.Description,
                       "https://ik.imagekit.io/rqlzhe7ko/birdseye-beach-view-1.webp?tr=w-1000", null, new List<OvalContainerComponent>()
                       {
                             new OvalContainerComponent("hostelPageIntroductionOvals1", 15, null, null, -28),
                             new OvalContainerComponent("hostelPageIntroductionOvals2", null, 15, null, 18)
                       })
                    {  AnimationStart = "onLoad"};
                    ToursCards = WebComponentsBuilder.FuseHostelsAndTravel.GetToursCarouselCards(Tours, "onScroll", $"TOURS IN {pageTitle.ToUpper()}", null);
                    AccommodationCards = WebComponentsBuilder.FuseHostelsAndTravel.GetHostelAccommodationCards(Property.Rooms, "onScroll");
                    
                    FacilitiesTable = WebComponentsBuilder.FuseHostelsAndTravel.GetHostelFacilities(Property);

                    NavPills = new List<NavPill>()
                    {
                        new NavPill("ABOUT", 1400),
                        new NavPill("ACCOMMODATION", 1600),
                        new NavPill($"TOURS IN {pageTitle.ToUpper()}", 1800),
                        new NavPill($"GETTING TO {pageTitle.ToUpper()}", 2000),
                    };
                }

                BookNowBanner = new BookNowComponent(Properties, Property.Id);
            }

            return Page();
        }
    }
}
