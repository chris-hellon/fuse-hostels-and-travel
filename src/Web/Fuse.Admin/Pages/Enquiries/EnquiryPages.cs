using System.Security.Claims;
using MudBlazor;
using Fuse.Shared.Authorization;

namespace Fuse.Admin.Pages.Enquiries;

/// <summary>What the enquiry lists share: the replied filter's choices and opening an enquiry.</summary>
internal static class EnquiryPages
{
    public static readonly DialogOptions DialogOptions = new() { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseButton = true };

    public static readonly DialogOptions ConversationOptions = new() { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true, CloseButton = true };

    public static readonly IReadOnlyList<Contracts.Enquiries.EnquiryStatus> Statuses =
        [Contracts.Enquiries.EnquiryStatus.New, Contracts.Enquiries.EnquiryStatus.WaitingOnUs, Contracts.Enquiries.EnquiryStatus.Replied, Contracts.Enquiries.EnquiryStatus.Closed];

    public static string Label(Contracts.Enquiries.EnquiryStatus status) => status switch
    {
        Contracts.Enquiries.EnquiryStatus.WaitingOnUs => "Waiting on us",
        Contracts.Enquiries.EnquiryStatus.Replied => "Replied",
        Contracts.Enquiries.EnquiryStatus.Closed => "Closed",
        _ => "New",
    };

    /// <summary>The status chip's look (see admin.css .enquiry-status--*).</summary>
    public static string CssClass(Contracts.Enquiries.EnquiryStatus status) => $"enquiry-status enquiry-status--{status.ToString().ToLowerInvariant()}";

    public static bool CanUpdate(ClaimsPrincipal? user) =>
        user?.HasPermission(FuseAction.Update, FuseResource.Enquiries) == true;
}
