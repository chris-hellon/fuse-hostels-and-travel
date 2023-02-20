namespace FuseHostelsAndTravel.Web.Pages.PrivacyPolicy
{
    public class IndexModel : BasePageModel
    {
        public IEnumerable<OvalContainerComponent> OvalContainers { get; private set; } = null;

        public async Task<IActionResult> OnGetAsync()
        {
            await base.OnGetDataAsync();

            OvalContainers = new List<OvalContainerComponent>()
            {
                new OvalContainerComponent("privacyPolicyPageOvals1", -8, null, -18, null),
                new OvalContainerComponent("privacyPolicyPageOvals2", null, -8, null, -18)
            };

            return Page();
        }
    }
}
