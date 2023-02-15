using Microsoft.AspNetCore.Components.Web;

namespace FuseHostelsAndTravel.Web.Pages.Home;

public class IndexModel : BasePageModel
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

    public IndexModel()
    {

    }

    public async Task<IActionResult> OnGetAsync()
    {
        await base.OnGetDataAsync();

        IntroductionBanner = new ContainerHalfImageRoundedTextComponent(new List<string>() { "THE LATEST ACCOMMODATION", "& TRAVEL EXPERIENCES", "IN VIETNAM" }, null, "Opening in 2 impressive locations in Hoi An in late 2022 and with over 15 years’ experience in Vietnam the FUSE Crew offer up everything travelers need to explore, kick back and have fun in Vietnam.",
            "https://images.unsplash.com/photo-1555979864-7a8f9b4fddf8?ixlib=rb-4.0.3&ixid=MnwxMjA3fDB8MHxwaG90by1wYWdlfHx8fGVufDB8fHx8&auto=format&fit=crop&w=3542&q=80", new ButtonComponent("/Hostels/Index", "View Locations"), new List<OvalContainerComponent>()
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

        HostelsContainers = WebComponentsBuilder.GetHostelsContainers(Hostels);
        ToursCards = WebComponentsBuilder.GetToursCards(Tours);
        ToursCarousel = WebComponentsBuilder.GetToursCarouselCards(Tours, "onScroll", "TAILORED TOURS");
        EventsCards = WebComponentsBuilder.GetEventsCarouselCards(Events);
        CarouselComponent = new FullImageCarouselComponent(await MockData.GetHomePageCarouselImages(), new BookNowComponent(Hostels, null, true));

        return Page();
    }
}

