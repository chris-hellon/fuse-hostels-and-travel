using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuseHostelsAndTravel.Web.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LockoutModel : BasePageModel
    {
        public async Task OnGet()
        {
            await base.OnGetDataAsync();
        }
    }
}
