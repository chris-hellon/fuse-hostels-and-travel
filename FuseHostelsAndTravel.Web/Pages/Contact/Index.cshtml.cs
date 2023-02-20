using AspNetCore.ReCaptcha;

namespace FuseHostelsAndTravel.Web.Pages.Contact
{
    [ValidateReCaptcha]
    public class IndexModel : BasePageModel
    {
        public IEnumerable<OvalContainerComponent> OvalContainers { get; private set; } = null;

        [BindProperty]
        [Required(ErrorMessage = "Please enter a Name.")]
        public string Name { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Please enter an Email Address.")]
        [DataType(DataType.EmailAddress)]
        public string Email { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Please enter a Message.")]
        public string Message { get; set; } = string.Empty;

        [BindProperty]
        [DataType(DataType.EmailAddress)]
        public string HoneyPot { get; set; } = string.Empty;

        public async Task<IActionResult> OnGetAsync()
        {
            await base.OnGetDataAsync();

            OvalContainers = new List<OvalContainerComponent>()
            {
                new OvalContainerComponent("contactPageOvals1", -20, null, -18, null),
                new OvalContainerComponent("contactPageOvals2", null, -20, null, -18)
            };

            return Page();
        }

        public async Task<IActionResult> OnPostAsync([FromServices] IReCaptchaService service)
        {
            if (!ModelState.IsValid)
            {
                ViewData["ErrorMessage"] = "<p>Please complete the Google Captcha to send your message.</p>";
                return await OnGetAsync();
            }

            if (!string.IsNullOrEmpty(HoneyPot))
            {
                ViewData["ErrorMessage"] = "<p>Suspicious activity detected.</p>";
                return await OnGetAsync();
            }

            return await OnGetAsync();
        }
    }
}
