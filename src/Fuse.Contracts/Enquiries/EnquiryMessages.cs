namespace Fuse.Contracts.Enquiries;

// An enquiry's email conversation: replies written in the admin (sent from the team's address) and what the inbox holds
// in the same conversation (the enquirer's replies, and replies sent from the inbox), oldest first.

public enum EnquiryMessageKind
{
    /// <summary>From us to the enquirer.</summary>
    Sent,
    /// <summary>From the enquirer.</summary>
    Received,
}

public sealed record EnquiryMessageView
{
    public DefaultIdType Id { get; init; }
    public EnquiryMessageKind Kind { get; init; }
    public string From { get; init; } = "";
    public string? FromName { get; init; }
    public string To { get; init; } = "";
    public string Subject { get; init; } = "";
    /// <summary>Safe to show as HTML.</summary>
    public string HtmlBody { get; init; } = "";
    /// <summary>Vietnam time.</summary>
    public DateTime SentOn { get; init; }
    /// <summary>The staff member who sent it from the admin (null for the enquirer's, or ones sent from the inbox).</summary>
    public string? SentByName { get; init; }
}

public sealed record EnquiryConversation
{
    public IReadOnlyList<EnquiryMessageView> Messages { get; init; } = [];
    /// <summary>The subject a reply gets: "Re:" the conversation's.</summary>
    public string ReplySubject { get; init; } = "";
    /// <summary>The enquirer's replies are read from the inbox (Gmail is set up); otherwise only what's sent from here shows.</summary>
    public bool ReadsInbox { get; init; }
    public EnquiryStatus Status { get; init; }
}

[Get("enquiries/{Kind}/{Id}/messages"), Permission(FuseAction.View, FuseResource.Enquiries)]
public sealed record GetEnquiryConversation(EnquiryKind Kind, DefaultIdType Id) : IQuery<EnquiryConversation>;

/// <summary>
/// Emails a reply to the enquirer from the team's address, saves it to the conversation and marks the enquiry replied.
/// The enquirer's answer comes back to the team's inbox, and into the conversation from there.
/// </summary>
[Post("enquiries/{Kind}/{Id}/messages"), Permission(FuseAction.Update, FuseResource.Enquiries)]
public sealed record ReplyToEnquiry(EnquiryKind Kind, DefaultIdType Id, string Subject, string HtmlBody) : ICommand;

/// <summary>Reads the inbox for this conversation now (it's also read every few minutes); returns how many emails were new.</summary>
[Post("enquiries/{Kind}/{Id}/messages/check"), Permission(FuseAction.View, FuseResource.Enquiries)]
public sealed record CheckEnquiryInbox(EnquiryKind Kind, DefaultIdType Id) : ICommand<int>;

public sealed class ReplyToEnquiryValidator : AbstractValidator<ReplyToEnquiry>
{
    public ReplyToEnquiryValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().WithMessage("Please enter a subject.").MaximumLength(500);
        RuleFor(x => x.HtmlBody).Must(HasText).WithMessage("Please write your reply.");
    }

    private static bool HasText(string? html) =>
        !string.IsNullOrWhiteSpace(System.Text.RegularExpressions.Regex.Replace(html ?? "", "<[^>]*>|&nbsp;", " "))
        || (html ?? "").Contains("<img", StringComparison.OrdinalIgnoreCase);
}
