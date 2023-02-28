using Microsoft.AspNetCore.Components.Web;

namespace FuseHostelsAndTravel.Web.Pages.Home;

public class IndexModel : TravaloudBasePageModel
{
    [BindProperty]
    public List<ContainerHalfImageTextComponent> HostelsContainers { get; private set; } = new List<ContainerHalfImageTextComponent>();

    [BindProperty]
    public GenericBannerComponent HostelsInVietnamBanner { get; private set; }

    [BindProperty]
    public ContainerHalfImageRoundedTextComponent IntroductionBanner { get; private set; }

    [BindProperty]
    public AlternatingCardHeightComponent ToursCards { get; private set; }

    [BindProperty]
    public CarouselCardsComponent ToursCarousel { get; private set; }

    [BindProperty]
    public CarouselCardsComponent EventsCards { get; private set; }

    [BindProperty]
    public FullImageCarouselComponent CarouselComponent { get; private set; }

    [BindProperty]
    public OvalContainerComponent MarqueeOvals { get; private set; }

    public IndexModel()
    {

    }

    public async Task<IActionResult> OnGetAsync()
    {
        await base.OnGetDataAsync();

        MarqueeOvals = new OvalContainerComponent("parallaxBannerOvals", 20, null, -10, null);

        IntroductionBanner = new ContainerHalfImageRoundedTextComponent(new List<string>() { "THE LATEST ACCOMMODATION", "& TRAVEL EXPERIENCES", "IN VIETNAM" }, null, "Opening in 2 impressive locations in Hoi An in late 2022 and with over 15 years’ experience in Vietnam the FUSE Crew offer up everything travelers need to explore, kick back and have fun in Vietnam.",
            "https://ik.imagekit.io/rqlzhe7ko/birdseye-beach-view-1.webp?tr=w-1000", new ButtonComponent("/Hostels/Index", "View Locations"), new List<OvalContainerComponent>()
            {
                 new OvalContainerComponent("homePageIntroductionOvals1", -40, null, null, -14),
                 new OvalContainerComponent("homePageIntroductionOvals2", null, -7, null, 36),
                 new OvalContainerComponent("homePageIntroductionOvals3", null, -30, null, -19)
            });

        HostelsInVietnamBanner = new GenericBannerComponent(
            "HOSTELS IN VIETNAM",
            "Fun, adventure and good vibes is the name of the game at FUSE and all locations come with a variety of cool social spaces and chill out spots.",
            new List<OvalContainerComponent>()
            {
                new OvalContainerComponent("homePageHostelsOvals", -70, null, -20, null)
            }
        );

        HostelsContainers = WebComponentsBuilder.FuseHostelsAndTravel.GetHostelsContainers(Properties);
        ToursCards = WebComponentsBuilder.FuseHostelsAndTravel.GetToursCards(Tours);
        ToursCarousel = WebComponentsBuilder.FuseHostelsAndTravel.GetToursCarouselCards(Tours, "onScroll", "TAILORED TOURS");
        EventsCards = WebComponentsBuilder.FuseHostelsAndTravel.GetEventsCarouselCards(Events);
        CarouselComponent = new FullImageCarouselComponent(await GetHomePageCarouselImages(), new BookNowComponent(Properties, null, true));

        return Page();
    }

    public async Task<List<Image>> GetHomePageCarouselImages()
    {
        var images = new List<Image>();

        await Task.Run(() =>
        {
            images = new List<Image>()
                {
                    new Image("https://ik.imagekit.io/rqlzhe7ko/home-page-banner-1.webp?tr=w-2000", new Guid("F28C2D2C-1C90-40A5-B13E-31D323C14E1E"), "Page", "HOSTEL<br/>EXPERIENCE", "THE ULTIMATE", "BEGINS AT FUSE", "")
                };
        });

        return images;
    }
}

