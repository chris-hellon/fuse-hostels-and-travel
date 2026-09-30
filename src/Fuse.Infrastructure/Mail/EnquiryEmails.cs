using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Fuse.Application.Common.Mail;
using Fuse.Infrastructure.Mail.Templates;

namespace Fuse.Infrastructure.Mail;

internal sealed class EnquiryEmails(IServiceProvider services, ILoggerFactory logs, IOptions<MailBrand> brand) : IEnquiryEmails
{
    public async Task<EmailMessage> RenderAsync(EnquiryEmail enquiry, string to, string replyTo) =>
        new([to], enquiry.Subject, await EmailRenderer.RenderAsync<Enquiry>(services, logs, new()
        {
            [nameof(Enquiry.Brand)] = brand.Value,
            [nameof(Enquiry.Email)] = enquiry,
        }))
        {
            ReplyTo = replyTo,
        };
}
