namespace Fuse.Domain.Catalog.Enquiries;

/// <summary>Which enquiry screen a message belongs to (stored as its number: the order never changes).</summary>
public enum EnquiryType
{
    General = 0,
    Tour = 1,
    JobApplication = 2,
}

public enum EnquiryMessageDirection
{
    /// <summary>The website's email of the enquiry to the team: not shown, but replies to it from the inbox thread with it.</summary>
    Notification = 0,
    /// <summary>From us to the enquirer (from the admin, or straight from the inbox).</summary>
    Sent = 1,
    /// <summary>From the enquirer, read from the inbox.</summary>
    Received = 2,
}

/// <summary>
/// One email of an enquiry's conversation: replies sent from the admin, and what the inbox (Gmail) holds in the same
/// conversation. Matched up by the email's Message-ID and Gmail's own ids, so each is stored once.
/// </summary>
public class EnquiryMessage : BaseEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private EnquiryMessage() { }

    public EnquiryMessage(EnquiryType enquiryType, DefaultIdType enquiryId, EnquiryMessageDirection direction, string fromAddress, string? fromName,
        string toAddress, string subject, string htmlBody, DateTime sentOnUtc, string? messageId, DefaultIdType? sentBy = null)
    {
        EnquiryType = enquiryType;
        EnquiryId = enquiryId;
        Direction = direction;
        FromAddress = Trim(fromAddress, 320);
        FromName = fromName is null ? null : Trim(fromName, 320);
        ToAddress = Trim(toAddress, 1000);
        Subject = Trim(subject, 500);
        HtmlBody = htmlBody;
        SentOn = sentOnUtc;
        MessageId = messageId is null ? null : Trim(messageId, 500);
        SentBy = sentBy;
    }

    public EnquiryType EnquiryType { get; private set; }
    public DefaultIdType EnquiryId { get; private set; }
    public EnquiryMessageDirection Direction { get; private set; }

    [MaxLength(320)] public string FromAddress { get; private set; } = "";
    [MaxLength(320)] public string? FromName { get; private set; }
    [MaxLength(1000)] public string ToAddress { get; private set; } = "";
    [MaxLength(500)] public string Subject { get; private set; } = "";

    /// <summary>Safe to show: written in the admin's editor, or cleaned (and its quoted history cut) when read from the inbox.</summary>
    public string HtmlBody { get; private set; } = "";

    /// <summary>UTC.</summary>
    public DateTime SentOn { get; private set; }

    /// <summary>The email's Message-ID header (without angle brackets).</summary>
    [MaxLength(500)] public string? MessageId { get; private set; }

    [MaxLength(64)] public string? GmailMessageId { get; private set; }
    [MaxLength(64)] public string? GmailThreadId { get; private set; }

    /// <summary>The staff member who sent it from the admin.</summary>
    public DefaultIdType? SentBy { get; private set; }

    public EnquiryMessage InGmail(string gmailMessageId, string gmailThreadId)
    {
        GmailMessageId = gmailMessageId;
        GmailThreadId = gmailThreadId;
        return this;
    }

    public EnquiryMessage InThread(string gmailThreadId)
    {
        GmailThreadId = gmailThreadId;
        return this;
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>Where an enquiry's conversation stands (stored as its number: the order never changes).</summary>
public enum EnquiryStage
{
    /// <summary>Nothing sent to the enquirer yet.</summary>
    New = 0,
    /// <summary>The enquirer wrote last.</summary>
    WaitingOnUs = 1,
    /// <summary>We wrote last.</summary>
    Replied = 2,
    /// <summary>Done, set by hand; the enquirer writing again reopens it.</summary>
    Closed = 3,
}

/// <summary>
/// An enquiry's status, once it has one (until then: Replied when legacy's ResponseSent flag says so, else New).
/// Kept beside the enquiry tables, which stay as legacy has them; ResponseSent follows it.
/// </summary>
public class EnquiryState : BaseEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private EnquiryState() { }

    public EnquiryState(EnquiryType enquiryType, DefaultIdType enquiryId, EnquiryStage stage, DateTime changedOnUtc)
    {
        EnquiryType = enquiryType;
        EnquiryId = enquiryId;
        Set(stage, changedOnUtc);
    }

    public EnquiryType EnquiryType { get; private set; }
    public DefaultIdType EnquiryId { get; private set; }
    public EnquiryStage Stage { get; private set; }

    /// <summary>UTC.</summary>
    public DateTime ChangedOn { get; private set; }

    public void Set(EnquiryStage stage, DateTime changedOnUtc) => (Stage, ChangedOn) = (stage, changedOnUtc);
}
