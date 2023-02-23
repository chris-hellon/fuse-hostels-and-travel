namespace FuseHostelsAndTravel.Web.Pages.OurStory
{
	public class IndexModel : TravaloudBasePageModel
    {
        [BindProperty]
        public HeaderBannerComponent HeaderBanner { get; private set; }

        [BindProperty]
        public ContainerHalfImageRoundedTextComponent IntroductionBanner { get; private set; }

        [BindProperty]
        public ContainerHalfImageRoundedTextComponent AboutBanner { get; private set; }

        [BindProperty]
        public List<NavPill> NavPills { get; private set; }

        public async Task<IActionResult> OnGetAsync()
        {
            await base.OnGetDataAsync();

            NavPills = new List<NavPill>()
            {
                new NavPill("WHO WE ARE", 1400),
                new NavPill("WHAT WE DO", 1600),
                new NavPill("OUR CORE VALUES", 1800)
            };

            HeaderBanner = new HeaderBannerComponent("FUSE?", "WHO ARE", null, "https://fusehostelsandtravel.azureedge.net/images/about-us-banner.webp", new List<OvalContainerComponent>()
                       {
                             new OvalContainerComponent("aboutPageHeaderBannerOvals1", 15, null, -30, null)
                       });

            IntroductionBanner = new ContainerHalfImageRoundedTextComponent(new List<string>() { "WHO WE ARE" }, null, "<p>Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.</p><p>Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, in culpa qui officia deserunt mollit.</p>",
                       "https://fusehostelsandtravel.azureedge.net/images/fuse-purple-logo.webp", null, new List<OvalContainerComponent>()
                       {
                             new OvalContainerComponent("aboutPageIntroductionOvals1", 15, null, null, -28),
                             new OvalContainerComponent("aboutPageIntroductionOvals2", null, 15, null, 18)
                       })
            { AnimationStart = "onLoad"};

            AboutBanner = new ContainerHalfImageRoundedTextComponent(new List<string>() { "WHAT WE DO" }, null, "<p>Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.</p><p>Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, in culpa qui officia deserunt mollit.</p>",
                     "https://fusehostelsandtravel.azureedge.net/images/fuse-staff-1.webp", null, new List<OvalContainerComponent>()
                     {
                             new OvalContainerComponent("aboutPageAboutOvals1", -40, null, -20, null),
                             new OvalContainerComponent("aboutPageAboutOvals2", null, -40, null, -20)
                     }, true);

            return Page();
        }
    }
}
