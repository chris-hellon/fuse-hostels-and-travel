using System.Net;
using Microsoft.Extensions.Options;
using Fuse.Application.Common.Mail;
using Fuse.Application.Common.Time;
using Fuse.Application.Features.Site;
using Fuse.Contracts.Enquiries;
using Fuse.Domain.Catalog.Enquiries;
using Fuse.Domain.Identity;

namespace Fuse.Application.Features.Enquiries;

// Replying to an enquiry from the admin, and its email conversation: replies go out from the team's address (threaded,
// with the enquirer's last words quoted), and the team's inbox is read for the enquirer's answers (EnquiryInbox).

/// <summary>Who an enquiry is from and what they wrote, for replying to it.</summary>
internal sealed record EnquiryContact(string Email, string Name, DateTime SubmittedUtc, string Subject, string QuoteHtml);

internal static class EnquiryMail
{
    public static EnquiryType Type(EnquiryKind kind) => (EnquiryType)(int)kind;

    public static async Task<EnquiryContact> ContactAsync(IAppDbContext db, string siteName, EnquiryKind kind, DefaultIdType id, CancellationToken cancellationToken)
    {
        var site = string.IsNullOrWhiteSpace(siteName) ? "Fuse Hostels & Travel" : siteName;
        switch (kind)
        {
            case EnquiryKind.General:
            {
                var e = await db.GeneralEnquiries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                        ?? throw new NotFoundException("Enquiry not found.");
                return new(e.Email, e.Name, e.CreatedOn, $"Your enquiry to {site}", Paragraphs(e.Message));
            }
            case EnquiryKind.Tour:
            {
                var e = await db.TourEnquiries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                        ?? throw new NotFoundException("Enquiry not found.");
                var tour = await db.Tours.IgnoreQueryFilters().Where(t => t.Id == e.TourId).Select(t => t.Name).FirstOrDefaultAsync(cancellationToken) ?? "a tour";
                var quote = Paragraphs($"{tour}, {e.RequestedDate:d MMMM yyyy}, {e.NumberOfPeople} {(e.NumberOfPeople == 1 ? "person" : "people")}")
                            + (string.IsNullOrWhiteSpace(e.AdditionalInformation) ? "" : Paragraphs(e.AdditionalInformation));
                return new(e.Email, e.Name, e.CreatedOn, $"Your enquiry about {tour}", quote);
            }
            default:
            {
                var e = await db.JobVacancyResponses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                        ?? throw new NotFoundException("Application not found.");
                var title = await db.JobVacancies.IgnoreQueryFilters().Where(v => v.Id == e.JobVacancyId).Select(v => v.JobTitle).FirstOrDefaultAsync(cancellationToken)
                            ?? "a position";
                return new(e.Email, $"{e.FirstName} {e.LastName}".Trim(), e.CreatedOn, $"Your application for {title}", Paragraphs(e.HowCanWeCollaborate));
            }
        }
    }

    /// <summary>
    /// Sets an enquiry's status, and legacy's ResponseSent flag with it (general and tour enquiries: replied when Replied or
    /// Closed). Saved by the caller.
    /// </summary>
    public static async Task SetStageAsync(IAppDbContext db, EnquiryType type, DefaultIdType id, EnquiryStage stage, CancellationToken cancellationToken)
    {
        var state = db.EnquiryStates.Local.FirstOrDefault(s => s.EnquiryType == type && s.EnquiryId == id)
                    ?? await db.EnquiryStates.FirstOrDefaultAsync(s => s.EnquiryType == type && s.EnquiryId == id, cancellationToken);
        if (state is null)
            db.EnquiryStates.Add(new EnquiryState(type, id, stage, DateTime.UtcNow));
        else
            state.Set(stage, DateTime.UtcNow);

        var replied = stage is EnquiryStage.Replied or EnquiryStage.Closed;
        if (type == EnquiryType.General && await db.GeneralEnquiries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken) is { } general)
            general.Update(null, null, null, null, replied);
        else if (type == EnquiryType.Tour && await db.TourEnquiries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken) is { } tour)
            tour.Update(null, null, null, null, null, null, null, replied);
    }

    /// <summary>Its current status: its own once set, else legacy's ResponseSent flag (Replied) or New.</summary>
    public static async Task<EnquiryStage> StageAsync(IAppDbContext db, EnquiryType type, DefaultIdType id, CancellationToken cancellationToken)
    {
        if (await db.EnquiryStates.AsNoTracking().Where(s => s.EnquiryType == type && s.EnquiryId == id).Select(s => (EnquiryStage?)s.Stage).FirstOrDefaultAsync(cancellationToken) is { } stage)
            return stage;
        var replied = type switch
        {
            EnquiryType.General => await db.GeneralEnquiries.AsNoTracking().Where(e => e.Id == id).Select(e => e.ResponseSent).FirstOrDefaultAsync(cancellationToken),
            EnquiryType.Tour => await db.TourEnquiries.AsNoTracking().Where(e => e.Id == id).Select(e => e.ResponseSent).FirstOrDefaultAsync(cancellationToken),
            _ => false,
        };
        return replied ? EnquiryStage.Replied : EnquiryStage.New;
    }

    public static async Task EnsureExistsAsync(IAppDbContext db, EnquiryKind kind, DefaultIdType id, CancellationToken cancellationToken)
    {
        var exists = kind switch
        {
            EnquiryKind.General => await db.GeneralEnquiries.AnyAsync(e => e.Id == id, cancellationToken),
            EnquiryKind.Tour => await db.TourEnquiries.AnyAsync(e => e.Id == id, cancellationToken),
            _ => await db.JobVacancyResponses.AnyAsync(e => e.Id == id, cancellationToken),
        };
        if (!exists)
            throw new NotFoundException(kind == EnquiryKind.JobApplication ? "Application not found." : "Enquiry not found.");
    }

    /// <summary>A new Message-ID on the team's domain (without angle brackets).</summary>
    public static string NewMessageId(SiteOptions site)
    {
        var at = site.ContactEmail?.IndexOf('@') ?? -1;
        var domain = at > 0 ? site.ContactEmail![(at + 1)..].Trim() : "fusehostelsandtravel.com";
        return $"{Guid.NewGuid():N}@{domain}";
    }

    public static string Re(string subject) =>
        subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? subject : $"Re: {subject}";

    /// <summary>Plain text as HTML paragraphs (encoded).</summary>
    public static string Paragraphs(string? text) =>
        string.Concat((text ?? "").Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => $"<p>{WebUtility.HtmlEncode(p.Trim()).Replace("\n", "<br>")}</p>"));
}

internal sealed class GetEnquiryConversationHandler(IAppDbContext db, IMailbox mailbox, IOptions<SiteOptions> site)
    : IQueryHandler<GetEnquiryConversation, EnquiryConversation>
{
    public async ValueTask<EnquiryConversation> Handle(GetEnquiryConversation query, CancellationToken cancellationToken)
    {
        var contact = await EnquiryMail.ContactAsync(db, site.Value.SiteName, query.Kind, query.Id, cancellationToken);
        var type = EnquiryMail.Type(query.Kind);
        var rows = await db.EnquiryMessages.AsNoTracking()
            .Where(m => m.EnquiryType == type && m.EnquiryId == query.Id && m.Direction != EnquiryMessageDirection.Notification)
            .OrderBy(m => m.SentOn)
            .ToListAsync(cancellationToken);
        var staffIds = rows.Where(m => m.SentBy != null).Select(m => m.SentBy!.Value.ToString()).Distinct().ToList();
        var staff = await db.Users.AsNoTracking().Where(u => staffIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => PersonNames.Display(u.FirstName, u.LastName, u.FullName), StringComparer.OrdinalIgnoreCase, cancellationToken);

        return new EnquiryConversation
        {
            Messages =
            [
                .. rows.Select(m => new EnquiryMessageView
                {
                    Id = m.Id,
                    Kind = m.Direction == EnquiryMessageDirection.Received ? EnquiryMessageKind.Received : EnquiryMessageKind.Sent,
                    From = m.FromAddress,
                    FromName = m.FromName,
                    To = m.ToAddress,
                    Subject = m.Subject,
                    HtmlBody = m.HtmlBody,
                    SentOn = BusinessClock.FromUtc(m.SentOn),
                    SentByName = m.SentBy is { } by ? staff.GetValueOrDefault(by.ToString()) : null,
                }),
            ],
            ReplySubject = EnquiryMail.Re(rows.LastOrDefault()?.Subject ?? contact.Subject),
            ReadsInbox = mailbox.IsEnabled,
            Status = (EnquiryStatus)(int)await EnquiryMail.StageAsync(db, type, query.Id, cancellationToken),
        };
    }
}

internal sealed class ReplyToEnquiryHandler(IAppDbContext db, IEmailSender sender, IMailbox mailbox, ICurrentUser user, IOptions<SiteOptions> site)
    : ICommandHandler<ReplyToEnquiry>
{
    public async ValueTask<Unit> Handle(ReplyToEnquiry command, CancellationToken cancellationToken)
    {
        var contact = await EnquiryMail.ContactAsync(db, site.Value.SiteName, command.Kind, command.Id, cancellationToken);
        if (string.IsNullOrWhiteSpace(contact.Email))
            throw new BusinessRuleException("This enquiry has no email address to reply to.");
        var type = EnquiryMail.Type(command.Kind);
        var thread = await db.EnquiryMessages.AsNoTracking()
            .Where(m => m.EnquiryType == type && m.EnquiryId == command.Id)
            .OrderBy(m => m.SentOn)
            .ToListAsync(cancellationToken);

        // Threaded under the conversation so far (mail apps group it), answering the enquirer's last email if there is one.
        var references = thread.Where(m => m.MessageId != null).Select(m => m.MessageId!).ToList();
        var answering = thread.LastOrDefault(m => m.Direction == EnquiryMessageDirection.Received && m.MessageId != null)
                        ?? thread.LastOrDefault(m => m.Direction != EnquiryMessageDirection.Notification && m.MessageId != null);
        var quoted = thread.LastOrDefault(m => m.Direction == EnquiryMessageDirection.Received);
        var (quoteName, quoteOn, quoteHtml) = quoted is not null
            ? (quoted.FromName ?? quoted.FromAddress, quoted.SentOn, quoted.HtmlBody)
            : (contact.Name, contact.SubmittedUtc, contact.QuoteHtml);

        var subject = command.Subject.Trim();
        var messageId = EnquiryMail.NewMessageId(site.Value);
        var html = $"""
            <div style="font-family: Arial, Helvetica, sans-serif; font-size: 14px; line-height: 1.5; color: #222;">{command.HtmlBody}</div>
            <br>
            <div style="font-family: Arial, Helvetica, sans-serif; font-size: 13px; color: #666;">On {BusinessClock.FromUtc(quoteOn):dddd d MMMM yyyy, h:mm tt}, {WebUtility.HtmlEncode(quoteName)} wrote:</div>
            <blockquote style="margin: 4px 0 0 0; padding-left: 10px; border-left: 2px solid #ccc; color: #666; font-family: Arial, Helvetica, sans-serif; font-size: 13px;">{quoteHtml}</blockquote>
            """;
        var replyTo = mailbox.Address ?? site.Value.ContactEmail;
        var sent = await sender.SendAsync(new EmailMessage([contact.Email], subject, html)
        {
            ReplyTo = string.IsNullOrWhiteSpace(replyTo) ? null : replyTo,
            MessageId = messageId,
            InReplyTo = answering?.MessageId,
            References = references,
        }, cancellationToken);
        if (!sent)
            throw new BusinessRuleException($"The reply wasn't sent: {contact.Email} can't be emailed from here (outside the live site, only allowed addresses get email).");

        db.EnquiryMessages.Add(new EnquiryMessage(type, command.Id, EnquiryMessageDirection.Sent, replyTo ?? "", site.Value.SiteName, contact.Email, subject,
            command.HtmlBody, DateTime.UtcNow, messageId, user.UserId));
        await EnquiryMail.SetStageAsync(db, type, command.Id, EnquiryStage.Replied, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class CheckEnquiryInboxHandler(EnquiryInbox inbox, IMailbox mailbox) : ICommandHandler<CheckEnquiryInbox, int>
{
    public async ValueTask<int> Handle(CheckEnquiryInbox command, CancellationToken cancellationToken)
    {
        if (!mailbox.IsEnabled)
            throw new BusinessRuleException("Replies can't be read here: the inbox (Mail:GmailOAuth) isn't set up.");
        try
        {
            return await inbox.SyncAsync(EnquiryMail.Type(command.Kind), command.Id, cancellationToken);
        }
        catch (HttpRequestException e) when (e.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
        {
            throw new BusinessRuleException(e.Message.Contains("has not been used", StringComparison.Ordinal) || e.Message.Contains("SERVICE_DISABLED", StringComparison.Ordinal)
                ? "The inbox can't be read yet: the Gmail API is turned off in the Google Cloud project the email login belongs to. Enable it there (APIs & Services, Gmail API) and try again in a few minutes."
                : "Gmail refused to let the inbox be read. Check the email login (Mail:GmailOAuth) still has access.");
        }
        catch (HttpRequestException)
        {
            throw new BusinessRuleException("Gmail couldn't be reached to check for replies. Try again in a moment.");
        }
    }
}

/// <summary>
/// Brings the team's inbox into enquiries' conversations: each conversation's Gmail threads (found from the Message-IDs
/// of the emails it holds) are read, and emails not yet in it are added (the enquirer's as received, the team's as sent).
/// A new email from the enquirer puts the enquiry back to "not replied"; one from the team marks it replied.
/// </summary>
internal sealed class EnquiryInbox(IAppDbContext db, IMailbox mailbox, IOptions<SiteOptions> site, ILogger<EnquiryInbox> logger)
{
    /// <summary>How long after its last email a conversation is still read.</summary>
    public static readonly TimeSpan Active = TimeSpan.FromDays(60);

    /// <summary>Every conversation with an email in the last <see cref="Active"/> (the scheduled job).</summary>
    public async Task<int> SyncRecentAsync(CancellationToken cancellationToken)
    {
        if (!mailbox.IsEnabled)
            return 0;
        var since = DateTime.UtcNow - Active;
        var conversations = await db.EnquiryMessages.AsNoTracking().Where(m => m.SentOn >= since)
            .Select(m => new { m.EnquiryType, m.EnquiryId }).Distinct().ToListAsync(cancellationToken);
        var added = 0;
        foreach (var c in conversations)
        {
            try
            {
                added += await SyncAsync(c.EnquiryType, c.EnquiryId, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // One conversation failing (Gmail refusing, a strange email) mustn't stop the rest.
                logger.LogWarning(e, "Couldn't read the inbox for {Type} enquiry {Id}", c.EnquiryType, c.EnquiryId);
            }
        }
        if (added > 0)
            logger.LogInformation("Enquiry inbox: {Count} new email(s)", added);
        return added;
    }

    public async Task<int> SyncAsync(EnquiryType type, DefaultIdType enquiryId, CancellationToken cancellationToken)
    {
        if (!mailbox.IsEnabled)
            return 0;
        var rows = await db.EnquiryMessages.Where(m => m.EnquiryType == type && m.EnquiryId == enquiryId).ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return 0;

        foreach (var row in rows.Where(r => r.GmailThreadId == null && r.MessageId != null))
            if (await mailbox.FindThreadAsync(row.MessageId!, cancellationToken) is { } found)
                row.InThread(found);

        var ours = new[] { mailbox.Address, site.Value.ContactEmail }.Where(a => !string.IsNullOrWhiteSpace(a)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = new List<EnquiryMessage>();
        foreach (var threadId in rows.Select(r => r.GmailThreadId).Where(t => t != null).Distinct().ToList())
        {
            foreach (var email in await mailbox.ReadThreadAsync(threadId!, cancellationToken))
            {
                if (rows.Any(r => r.GmailMessageId == email.GmailId))
                    continue;
                if (email.MessageId is { } id && rows.FirstOrDefault(r => string.Equals(r.MessageId, id, StringComparison.OrdinalIgnoreCase)) is { } mine)
                {
                    mine.InGmail(email.GmailId, email.ThreadId); // one of ours, now found in the inbox
                    continue;
                }
                var direction = ours.Contains(email.FromAddress) ? EnquiryMessageDirection.Sent : EnquiryMessageDirection.Received;
                var message = new EnquiryMessage(type, enquiryId, direction, email.FromAddress, email.FromName, email.To, email.Subject, email.HtmlBody,
                    email.SentOnUtc, email.MessageId).InGmail(email.GmailId, email.ThreadId);
                db.EnquiryMessages.Add(message);
                rows.Add(message);
                added.Add(message);
            }
        }

        if (added.Count > 0)
        {
            // Waiting for us when the enquirer wrote last; replied when the team did (from the inbox).
            var latest = rows.Where(r => r.Direction != EnquiryMessageDirection.Notification).MaxBy(r => r.SentOn)!;
            if (added.Contains(latest))
                await EnquiryMail.SetStageAsync(db, type, enquiryId,
                    latest.Direction == EnquiryMessageDirection.Sent ? EnquiryStage.Replied : EnquiryStage.WaitingOnUs, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        return added.Count;
    }
}
