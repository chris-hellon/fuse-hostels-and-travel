using System.Security.Claims;
using MudBlazor;
using Fuse.Shared.Authorization;

namespace Fuse.Admin.Pages.Enquiries;

/// <summary>What the enquiry lists share: the replied filter's choices and opening an enquiry.</summary>
internal static class EnquiryPages
{
    public static readonly DialogOptions DialogOptions = new() { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseButton = true };

    /// <summary>"All", "Not replied", "Replied" as the filter's values.</summary>
    public static readonly IReadOnlyList<(string Label, bool? Value)> RepliedChoices = [("All", null), ("Not replied yet", false), ("Replied", true)];

    public static bool CanUpdate(ClaimsPrincipal? user) =>
        user?.HasPermission(FuseAction.Update, FuseResource.Enquiries) == true;
}
