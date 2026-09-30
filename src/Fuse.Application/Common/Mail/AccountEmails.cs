namespace Fuse.Application.Common.Mail;

/// <summary>Renders the guest account emails, branded from Mail:Brand.</summary>
public interface IAccountEmails
{
    /// <summary>Legacy "Confirm your email": the link that makes <paramref name="to"/> the account's email.</summary>
    Task<EmailMessage> ConfirmEmailChangeAsync(string firstName, string to, string link);

    /// <summary>Legacy "Reset Password": the link to choose a new password.</summary>
    Task<EmailMessage> ResetPasswordAsync(string firstName, string to, string link);
}
