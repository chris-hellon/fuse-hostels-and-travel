using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Fuse.Application.Common.Mail;
using Fuse.Infrastructure.Mail.Templates;

namespace Fuse.Infrastructure.Mail;

internal sealed class AccountEmails(IServiceProvider services, ILoggerFactory logs, IOptions<MailBrand> brand) : IAccountEmails
{
    public async Task<EmailMessage> ConfirmEmailChangeAsync(string firstName, string to, string link) =>
        new([to], "Confirm your email", await EmailRenderer.RenderAsync<ConfirmEmailChange>(services, logs, new()
        {
            [nameof(ConfirmEmailChange.Brand)] = brand.Value,
            [nameof(ConfirmEmailChange.FirstName)] = firstName,
            [nameof(ConfirmEmailChange.Link)] = link,
        }));

    public async Task<EmailMessage> ConfirmEmailAsync(string firstName, string to, string link) =>
        new([to], "Confirm your email", await EmailRenderer.RenderAsync<ConfirmEmailChange>(services, logs, new()
        {
            [nameof(ConfirmEmailChange.Brand)] = brand.Value,
            [nameof(ConfirmEmailChange.FirstName)] = firstName,
            [nameof(ConfirmEmailChange.Link)] = link,
            [nameof(ConfirmEmailChange.Verification)] = true,
        }));

    public async Task<EmailMessage> ResetPasswordAsync(string firstName, string to, string link) =>
        new([to], "Reset your password", await EmailRenderer.RenderAsync<ResetPassword>(services, logs, new()
        {
            [nameof(ResetPassword.Brand)] = brand.Value,
            [nameof(ResetPassword.FirstName)] = firstName,
            [nameof(ResetPassword.Link)] = link,
        }));
}
