namespace Fuse.Application.Common.Mail;

/// <summary>
/// The team's inbox (Gmail, through the Google access Mail:GmailOAuth gives), read so an enquiry's conversation shows the
/// enquirer's replies. Read-only: sending is <see cref="IEmailSender"/>.
/// </summary>
public interface IMailbox
{
    /// <summary>Gmail is set up; without it, nothing is read.</summary>
    bool IsEnabled { get; }

    /// <summary>The inbox's own address (what our emails come from).</summary>
    string? Address { get; }

    /// <summary>The Gmail conversation holding the email with this Message-ID; null when it isn't in the inbox (yet).</summary>
    Task<string?> FindThreadAsync(string messageId, CancellationToken cancellationToken);

    /// <summary>Every email in a Gmail conversation, oldest first, their bodies cleaned and their quoted history cut.</summary>
    Task<IReadOnlyList<MailboxMessage>> ReadThreadAsync(string threadId, CancellationToken cancellationToken);
}

/// <param name="HtmlBody">Safe to show: cleaned of scripts and the like, with the quoted earlier emails cut off.</param>
/// <param name="SentOnUtc">When Gmail received (or sent) it.</param>
public sealed record MailboxMessage(string GmailId, string ThreadId, string? MessageId, string FromAddress, string? FromName, string To,
    string Subject, string HtmlBody, DateTime SentOnUtc);
