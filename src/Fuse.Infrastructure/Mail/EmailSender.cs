using MailKit.Net.Smtp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Fuse.Application.Common.Mail;

namespace Fuse.Infrastructure.Mail;

/// <summary>
/// Sends through SMTP (MailKit), or writes .eml files locally. Outside production SMTP only reaches
/// <see cref="MailOptions.AllowedRecipients"/>, and automated emails (<see cref="EmailMessage.Automated"/>, e.g. the
/// tour manifests) are never sent at all: only emails someone triggers. As legacy, never emails booking.com relays.
/// </summary>
internal sealed class EmailSender(IOptions<MailOptions> options, IHostEnvironment environment, ILogger<EmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var to = Deliverable(message.To, settings);
        var bcc = Deliverable(message.Bcc.Concat(settings.Bcc), settings);
        if (to.Count == 0)
        {
            logger.LogWarning("Email \"{Subject}\" not sent: none of its recipients may receive email here ({Recipients})",
                message.Subject, string.Join(", ", message.To));
            return;
        }

        var email = new MimeMessage { Subject = message.Subject };
        email.From.Add(new MailboxAddress(settings.DisplayName, settings.From));
        email.Sender = new MailboxAddress(settings.DisplayName, settings.From);
        foreach (var address in to)
            email.To.Add(MailboxAddress.Parse(address));
        foreach (var address in bcc)
            email.Bcc.Add(MailboxAddress.Parse(address));
        if (message.ReplyTo is { } replyTo && MailboxAddress.TryParse(replyTo, out var replyAddress))
            email.ReplyTo.Add(replyAddress);
        var body = new BodyBuilder { HtmlBody = message.HtmlBody };
        foreach (var file in message.Attachments)
            body.Attachments.Add(file.FileName, file.Content, ContentType.Parse(file.ContentType));
        email.Body = body.ToMessageBody();

        var automatedOutsideProduction = message.Automated && !environment.IsProduction();
        if (!settings.UsesSmtp || automatedOutsideProduction)
        {
            var folder = Path.Combine(environment.ContentRootPath, settings.PickupDirectory);
            Directory.CreateDirectory(folder);
            var name = string.Concat($"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{message.Subject}".Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var path = Path.Combine(folder, $"{name[..Math.Min(name.Length, 120)]}-{Guid.NewGuid():N}.eml");
            await email.WriteToAsync(path, cancellationToken);
            logger.LogInformation("Email \"{Subject}\" to {To} written to {Path} ({Reason})", message.Subject, string.Join(", ", to), path,
                automatedOutsideProduction ? "automated emails are only sent in production" : "Mail:Mode is Pickup");
            return;
        }

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(settings.Host, settings.Port, cancellationToken: cancellationToken);
        if (settings.Port != 25)
            await smtp.AuthenticateAsync(settings.UserName, settings.Password, cancellationToken);
        var response = await smtp.SendAsync(email, cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);
        logger.LogInformation("Email \"{Subject}\" to {To} accepted: {Response}", message.Subject, string.Join(", ", to), response);
    }

    private List<string> Deliverable(IEnumerable<string> addresses, MailOptions settings)
    {
        var restricted = settings.UsesSmtp && !environment.IsProduction();
        return addresses
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Where(a => !a.EndsWith("@booking.com", StringComparison.OrdinalIgnoreCase))
            .Where(a => !restricted || settings.AllowedRecipients.Contains(a, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
