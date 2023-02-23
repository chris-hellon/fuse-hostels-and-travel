namespace FuseHostelsAndTravel.Web.Pages.CookiePolicy
{
    public class IndexModel : TravaloudBasePageModel
    {
        public IEnumerable<OvalContainerComponent> OvalContainers { get; private set; } = null;

        public async Task<IActionResult> OnGetAsync()
        {
            await base.OnGetDataAsync();

            OvalContainers = new List<OvalContainerComponent>()
            {
                new OvalContainerComponent("cookiePolicyPageOvals1", -8, null, -18, null),
                new OvalContainerComponent("cookiePolicyPageOvals2", null, -8, null, -18)
            };

            return Page();
        }
    }
}
