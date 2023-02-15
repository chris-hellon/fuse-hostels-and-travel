using Microsoft.AspNetCore.Mvc.Rendering;

namespace FuseHostelsAndTravel.Web.Models
{
	public class BasePageModel : PageModel
    {
        protected string _tenant => "fuse";

        private IHttpContextAccessor _httpContextAccessor;
        protected IHttpContextAccessor HttpContextAccessor => _httpContextAccessor ??= HttpContext.RequestServices.GetService<IHttpContextAccessor>();

        private IErrorLoggerService _errorLoggerService;
        protected IErrorLoggerService ErrorLoggerService => _errorLoggerService ??= HttpContext.RequestServices.GetService<IErrorLoggerService>();

        private IMockData _mockData;
        protected IMockData MockData => _mockData ??= HttpContext.RequestServices.GetService<IMockData>();

        private IUsersRepository _usersRepository;
        protected IUsersRepository UsersRepository => _usersRepository ??= HttpContext.RequestServices.GetService<IUsersRepository>();

        private SignInManager<ApplicationUser> _signInManager;
        protected SignInManager<ApplicationUser> SignInManager => _signInManager ??= HttpContext.RequestServices.GetService<SignInManager<ApplicationUser>>();

        private ApplicationUserManager<ApplicationUser> _userManager;
        protected ApplicationUserManager<ApplicationUser> UserManager => _userManager ??= HttpContext.RequestServices.GetService<ApplicationUserManager<ApplicationUser>>();

        private IUserStore<ApplicationUser> _userStore;
        protected IUserStore<ApplicationUser> UserStore => _userStore ??= HttpContext.RequestServices.GetService<IUserStore<ApplicationUser>>();

        private IUserEmailStore<ApplicationUser> _emailStore;
        protected IUserEmailStore<ApplicationUser> EmailStore => _emailStore ??= HttpContext.RequestServices.GetService<IUserEmailStore<ApplicationUser>>();

        public List<Hostel> Hostels { get; set; }
        public List<Tour> Tours { get; set; }
        public List<Event> Events { get; set; }

        [BindProperty]
        public LoginModalComponent LoginModal { get; set; } = new LoginModalComponent();

        [BindProperty]
        public RegisterModalComponent RegisterModal { get; set; } = new RegisterModalComponent();

        public BasePageModel()
        {
            
        }

        public async Task OnGetDataAsync()
        {
            var hostels = MockData.GetHostels();
            var tours = MockData.GetTours();
            var events = MockData.GetEvents();

            await Task.WhenAll(hostels, tours, events);

            Hostels = hostels.Result;
            Tours = tours.Result;
            Events = events.Result;
        }

        public async Task<IActionResult> OnPostSignInAsync()
        {
            var returnUrl = LoginModal.ReturnUrl;
            var result = await SignInManager.PasswordSignInAsync(LoginModal.Email, LoginModal.Password, LoginModal.RememberMe, lockoutOnFailure: false);
            if (result.Succeeded)
            {
                var userId = Helpers.GetUserId(HttpContextAccessor);

                if (returnUrl != null && returnUrl.Contains("hostel-booking"))
                    returnUrl = returnUrl += $"/{userId}";

                returnUrl = returnUrl ?? "/my-account/profile";
                return LocalRedirect(returnUrl);
            }
            if (result.RequiresTwoFactor)
            {
                returnUrl = $"/login-with-2fa{(returnUrl != null ? $"?returnUrl={returnUrl}" : "")}";
                return LocalRedirect(returnUrl);
            }
            if (result.IsLockedOut)
            {
                return RedirectToPage("/lockout");
            }
            else
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            }

            return Page();
        }

        public async Task<JsonResult> OnPostValidateUser([FromBody] ValidateUserDTO model)
        {
            string message = "<p><strong>We were unable to find a user with the details provided.</strong></p><p>Please ensure your details are correct, or Contact Us</p>";
            var user = await UserManager.FindByEmailAsync(model.Username);

            if (user != null)
            {
                var passwordCorrect = await UserManager.CheckPasswordAsync(user, model.Password);

                if (passwordCorrect)
                    return new JsonResult(new
                    {
                        Success = true,
                        Message = ""
                    });
                else message = "<p><strong>Your password is incorrect.</strong></p><p>Please ensure you've inserted the correct password, or reset your password.</p>";
            }

            return new JsonResult(new
            {
                Success = false,
                Message = message
            });
        }

        public async Task<JsonResult> OnPostCheckIfUserExists([FromBody] ValidateUserDTO model)
        {
            string message = "<p><strong>A user with this Email Address already exists.</strong></p><p>Please choose a different Email Address.</p>";
            var user = await UserManager.FindByEmailAsync(model.Username);

            if (user == null)
            {
                bool passwordOk = true;
                message = "<p><strong>Password validation failed.</strong>";

                foreach (IPasswordValidator<ApplicationUser> passwordValidator in UserManager.PasswordValidators)
                {
                    var result = await passwordValidator.ValidateAsync(UserManager, user, model.Password);
                    
                    if (!result.Succeeded)
                    {
                        foreach (var error in result.Errors)
                        {
                            message += $"<p>{error.Description}</p>";
                        }

                        passwordOk = false;
                    } 
                }

                if (passwordOk)
                    return new JsonResult(new
                    {
                        Success = true,
                        Message = ""
                    });
            }
                
            return new JsonResult(new
            {
                Success = false,
                Message = message
            });
        }

        public async Task<IActionResult> OnPostRegisterAsync()
        {
            var user = new ApplicationUser()
            {
                FirstName = RegisterModal.FirstName,
                LastName = RegisterModal.Surname,
                PhoneNumber = RegisterModal.PhoneNumber,
                Gender = RegisterModal.Gender,
                Nationality = RegisterModal.Nationality,
                DateOfBirth = RegisterModal.DateOfBirth,
                UserName = RegisterModal.Email,
                Email = RegisterModal.Email,
                SignUpDate = DateTime.Now,
                TenantId = _tenant,
                IsActive = true,
                RefreshTokenExpiryTime = DateTime.Now
            };

            var result = await UserManager.CreateAsync(user, RegisterModal.Password);

            if (result.Succeeded)
            {
                var roleId = await UsersRepository.GetRoleId("Guest");

                if (roleId != null)
                {
                    var userResult = await UserManager.AddToRoleByRoleIdAsync(user, roleId);

                    if (userResult.Succeeded)
                    {
                        await SignInManager.SignInAsync(user, isPersistent: false);

                        var returnUrl = RegisterModal.ReturnUrl;
                        if (returnUrl != null && returnUrl.Contains("hostel-booking"))
                            returnUrl = returnUrl += $"/{user.Id}";

                        returnUrl = returnUrl ?? "/my-account/profile";
                        return LocalRedirect(returnUrl);
                    }
                } 
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        public async Task<IActionResult> OnPostSearchRooms()
        {
            string userId = Helpers.GetUserId(HttpContextAccessor);

            var hostelId = GetFormValue("HostelId");
            var checkInDate = GetFormValue("CheckInDate");
            var checkOutDate = GetFormValue("CheckOutDate");

            if (hostelId != null && checkInDate != null && checkOutDate != null)
            {
                int hostelIdParsed = int.Parse(hostelId);
                DateTime checkInDateParsed = DateTime.Parse(checkInDate);
                DateTime checkOutDateParsed = DateTime.Parse(checkOutDate);

                var hostels = await MockData.GetHostels();
                var hostelName = hostels.FirstOrDefault(x => x.Id == hostelIdParsed).FriendlyUrl;

                var url = $"/hostel-booking/{hostelName}/{checkInDateParsed.ToString("yyyy-MM-dd").UrlFriendly()}/{checkOutDateParsed.ToString("yyyy-MM-dd").UrlFriendly()}{(userId != null ? $"/{userId}" : "")}";

                return LocalRedirect(url);
            }

            return LocalRedirect("/error");
        }

        private string GetFormValue(string key)
        {
            string returnValue = "";
            var parentKeys = new string[] { "CarouselComponent", "BookNowBanner" };

            foreach (var parentKey in parentKeys)
            {
                var keyValue = $"{parentKey}.{key}";
                if (Request.Form.ContainsKey($"{parentKey}.{key}"))
                    returnValue = Request.Form[keyValue].ToString();
            }

            if (returnValue == "" && Request.Form.ContainsKey(key))
                returnValue = Request.Form[key].ToString();

            return returnValue;
        }
    }
}

