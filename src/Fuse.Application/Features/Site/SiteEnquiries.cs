using System.Globalization;
using Microsoft.Extensions.Options;
using Fuse.Application.Common.Jobs;
using Fuse.Application.Common.Mail;
using Fuse.Contracts.Site;
using Fuse.Domain.Catalog.Enquiries;

namespace Fuse.Application.Features.Site;

// The website's enquiry forms: checked with reCAPTCHA, saved for the admin's Enquiries screens, and emailed to the
// team (Site:ContactEmail) with the visitor as the reply-to. Legacy did the same, bar the reply-to.

/// <summary>Shared by the enquiry handlers: the bot checks and the email to the team.</summary>
internal sealed class SiteEnquiries(IAppDbContext db, ICaptcha captcha, IEnquiryEmails emails, IBackgroundJobs jobs, IOptions<SiteOptions> options,
    ILogger<SiteEnquiries> logger)
{
    /// <summary>False for a bot that filled the hidden field: it's thanked as usual, and nothing is saved or sent.</summary>
    public async Task<bool> AcceptAsync(string? honeyPot, string response, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(honeyPot))
        {
            logger.LogInformation("Website enquiry ignored: the hidden field was filled in");
            return false;
        }
        if (!await captcha.VerifyAsync(response, cancellationToken))
            throw new BusinessRuleException(EnquiryMessages.Captcha);
        return true;
    }

    /// <summary>
    /// Emails the enquiry to the team. Its Message-ID is kept with the enquiry's conversation, so replies sent from the inbox
    /// to it (and the enquirer's answers) show in the admin too.
    /// </summary>
    public async Task SendAsync(EnquiryEmail enquiry, string replyTo, EnquiryType type, DefaultIdType enquiryId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ContactEmail))
        {
            logger.LogWarning("Website enquiry \"{Subject}\" saved but not emailed: Site:ContactEmail isn't set", enquiry.Subject);
            return;
        }
        var messageId = Enquiries.EnquiryMail.NewMessageId(options.Value);
        var email = await emails.RenderAsync(enquiry, options.Value.ContactEmail, replyTo) with { MessageId = messageId };
        jobs.EnqueueEmail(email);
        db.EnquiryMessages.Add(new EnquiryMessage(type, enquiryId, EnquiryMessageDirection.Notification, options.Value.ContactEmail, null,
            options.Value.ContactEmail, email.Subject, "", DateTime.UtcNow, messageId));
        await db.SaveChangesAsync(cancellationToken);
    }

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class SendEnquiryHandler(IAppDbContext db, SiteEnquiries enquiries) : ICommandHandler<SendEnquiry>
{
    public async ValueTask<Unit> Handle(SendEnquiry command, CancellationToken cancellationToken)
    {
        if (!await enquiries.AcceptAsync(command.Website, command.Captcha, cancellationToken))
            return Unit.Value;

        var (name, email, phone, message) = (command.Name.Trim(), command.Email.Trim(), command.ContactNumber.Trim(), command.Message.Trim());
        // Legacy kept the related hostel and tour in the email only (the enquiry has no place for them).
        var enquiry = new GeneralEnquiry(name, email, phone, message, false);
        db.GeneralEnquiries.Add(enquiry);
        await db.SaveChangesAsync(cancellationToken);

        await enquiries.SendAsync(new EnquiryEmail("Website Enquiry", "Website Enquiry",
        [
            new("Name", name),
            new("Email", email),
            new("Contact Number", phone),
            new("Related Hostel", SiteEnquiries.Clean(command.Hostel)),
            new("Related Tour", SiteEnquiries.Clean(command.Tour)),
            new("Message", message),
        ]), email, EnquiryType.General, enquiry.Id, cancellationToken);
        return Unit.Value;
    }
}

internal sealed class SendTourEnquiryHandler(IAppDbContext db, SiteEnquiries enquiries) : ICommandHandler<SendTourEnquiry>
{
    public async ValueTask<Unit> Handle(SendTourEnquiry command, CancellationToken cancellationToken)
    {
        var tour = await db.Tours.AsNoTracking()
            .Where(t => t.Id == command.TourId).Where(SiteBookableTours.Rule(db))
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("This tour could not be found.");
        if (!await enquiries.AcceptAsync(command.Website, command.Captcha, cancellationToken))
            return Unit.Value;

        var (name, email, phone, requests) = (command.Name.Trim(), command.Email.Trim(), command.ContactNumber.Trim(), SiteEnquiries.Clean(command.SpecialRequests));
        var date = command.Date!.Value.Date;
        var enquiry = new TourEnquiry(command.TourId, name, email, phone, date, command.People!.Value, requests, false);
        db.TourEnquiries.Add(enquiry);
        await db.SaveChangesAsync(cancellationToken);

        await enquiries.SendAsync(new EnquiryEmail("Tour Booking Enquiry", $"Tour Booking Enquiry: {tour}",
        [
            new("Tour", tour),
            new("Requested Date", date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)), // legacy ToShortDateString() under the app culture
            new("Number of People", command.People.Value.ToString()),
            new("Name", name),
            new("Email", email),
            new("Contact Number", phone),
            new("Special Requests", requests),
        ]), email, EnquiryType.Tour, enquiry.Id, cancellationToken);
        return Unit.Value;
    }
}
