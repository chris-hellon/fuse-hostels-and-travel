using AspNetCore.ReCaptcha;

namespace FuseHostelsAndTravel.Web.Pages.Contact
{
    [ValidateReCaptcha]
    public class IndexModel : BasePageModel
    {
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
