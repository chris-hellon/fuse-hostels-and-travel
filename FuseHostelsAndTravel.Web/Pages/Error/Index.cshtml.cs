using Microsoft.AspNetCore.Diagnostics;

namespace FuseHostelsAndTravel.Web.Pages.Error
{
	public class IndexModel : BasePageModel
    {
        public string RequestId { get; set; }
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
        public string ExceptionMessage { get; set; }
        public int? StatusCodeValue { get; set; }

        public async Task<IActionResult> OnGet(int? statusCode)
        {
            await base.OnGetDataAsync();

            StatusCodeValue = statusCode;

            var statusCodeReExecuteFeature = Request.HttpContext.Features.Get<IStatusCodeReExecuteFeature>();

            if (statusCode == 404 && statusCodeReExecuteFeature != null)
            {
                var path = statusCodeReExecuteFeature.OriginalPath;
                ErrorLoggerService.LogError($"Page not found: {path}");
            }

            var errorMessage = "An error occurred while processing your request.";

            if (statusCode.HasValue)
            {
                switch (statusCode.Value)
                {
                    case 404:
                        ViewData["Title"] = "LOST IN THE MATRIX?";
                        errorMessage = "The page you are looking for doesn't exist or has been moved.";
                        break;
                    case 401:
                        ViewData["Title"] = "UNAUTHORIZED";
                        errorMessage = "You are not authorized to access this page.";
                        break;
                    default:
                        ViewData["Title"] = "ERROR";
                        errorMessage = "An error occurred while processing your request.";
                        break;
                }
            }

            ExceptionMessage = errorMessage;

            return Page();
        }
    }
}
