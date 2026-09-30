namespace Fuse.Application.Common.Mail;

public sealed record EmailMessage(IReadOnlyList<string> To, string Subject, string HtmlBody)
{
    public IReadOnlyList<string> Bcc { get; init; } = [];
    /// <summary>Where replies go (an enquiry: the visitor), instead of the sending address.</summary>
    public string? ReplyTo { get; init; }
    /// <summary>
    /// Sent by a scheduled job rather than because someone did something (the tour manifests). Never sent outside
    /// production, whatever Mail:Mode says: written to the local mail folder instead.
    /// </summary>
    public bool Automated { get; init; }
    /// <summary>Files sent with it (e.g. a manifest spreadsheet).</summary>
    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];
}

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Sends email. Outside production nothing reaches a real inbox unless it's on the configured allow-list;
/// by default development writes messages to a local folder instead (see Mail settings).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public static class EmailQueue
{
    /// <summary>Sends in the background (retried), so a slow or failing mail server never fails the request.</summary>
    public static void EnqueueEmail(this Jobs.IBackgroundJobs jobs, EmailMessage message)
    {
        // Stored with its type names until sent: plain arrays, not the compiler's lists from [..] (which can't be read back).
        var stored = message with { To = message.To.ToArray(), Bcc = message.Bcc.ToArray(), Attachments = message.Attachments.ToArray() };
        jobs.Enqueue<IEmailSender>(sender => sender.SendAsync(stored, CancellationToken.None));
    }
}
