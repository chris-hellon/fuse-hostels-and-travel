using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Fuse.Application.Common.Jobs;
using Fuse.Application.Common.Mail;
using Fuse.Application.Features.Site;
using Fuse.Contracts.Identity;
using Fuse.Domain.Identity;

namespace Fuse.Application.Features.Identity;

internal sealed class RequestPasswordResetHandler(UserManager<ApplicationUser> users, IAccountEmails emails, IBackgroundJobs jobs,
    IOptions<SiteOptions> site, ILogger<RequestPasswordResetHandler> logger) : ICommandHandler<RequestPasswordReset>
{
    public async ValueTask<Unit> Handle(RequestPasswordReset command, CancellationToken cancellationToken)
    {
        // Nothing tells the caller whether the email has an account: it's always "check your email".
        // Staff are found as the back office signs them in: by username, then by email.
        var email = command.Email.Trim();
        var user = command.Staff
            ? await users.FindByNameAsync(email) ?? await users.FindByEmailAsync(email)
            : await users.FindByEmailAsync(email);
        if (user?.Email is null)
            return Unit.Value;
        if (command.Staff && (!user.IsActive || !(await users.GetRolesAsync(user)).Any(FuseRoles.StaffRoles.Contains)))
            return Unit.Value;

        await PasswordResetLinks.SendAsync(users, emails, jobs, site.Value, user, staff: command.Staff);
        logger.LogInformation("Password reset requested for user {UserId}", user.Id);
        return Unit.Value;
    }
}

/// <summary>The emailed single-use link to choose a new password (the website's page, or the back office's).</summary>
internal static class PasswordResetLinks
{
    public static async Task SendAsync(UserManager<ApplicationUser> users, IAccountEmails emails, IBackgroundJobs jobs, SiteOptions site,
        ApplicationUser user, bool staff)
    {
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var path = staff ? "/admin/reset-password" : "/reset-password";
        var link = $"{site.LinksUrl}{path}?user={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}";
        jobs.EnqueueEmail(await emails.ResetPasswordAsync(user.FirstName ?? "", user.Email!, link));
    }
}

/// <summary>
/// Legacy's "Send Password Reset Link" on the Staff and Suppliers lists: emails the account's own address a back-office
/// reset link, whether or not they're active (as legacy). An account without an email can't be sent one.
/// </summary>
internal sealed class StaffPasswordResetSender(UserManager<ApplicationUser> users, IAccountEmails emails, IBackgroundJobs jobs,
    IOptions<SiteOptions> site, ILogger<StaffPasswordResetSender> logger)
{
    public async Task SendAsync(ApplicationUser user)
    {
        if (PlaceholderEmails.Visible(user.Email) is null || string.IsNullOrWhiteSpace(user.Email))
            throw new BusinessRuleException("This account has no email address to send a reset link to.");
        await PasswordResetLinks.SendAsync(users, emails, jobs, site.Value, user, staff: true);
        logger.LogInformation("Password reset link sent to user {UserId} by staff", user.Id);
    }
}

internal sealed class SendStaffPasswordResetHandler(UserKinds kinds, StaffPasswordResetSender sender) : ICommandHandler<SendStaffPasswordReset>
{
    public async ValueTask<Unit> Handle(SendStaffPasswordReset command, CancellationToken cancellationToken)
    {
        await sender.SendAsync(await kinds.FindAsync(command.Id, staff: true, cancellationToken));
        return Unit.Value;
    }
}

internal sealed class SendSupplierPasswordResetHandler(UserKinds kinds, UserManager<ApplicationUser> users, StaffPasswordResetSender sender)
    : ICommandHandler<SendSupplierPasswordReset>
{
    public async ValueTask<Unit> Handle(SendSupplierPasswordReset command, CancellationToken cancellationToken)
    {
        var user = await kinds.FindAsync(command.Id, staff: true, cancellationToken);
        if (!await users.IsInRoleAsync(user, FuseRoles.Supplier))
            throw new NotFoundException("Supplier not found.");
        await sender.SendAsync(user);
        return Unit.Value;
    }
}

internal sealed class ResetPasswordHandler(UserManager<ApplicationUser> users, ILogger<ResetPasswordHandler> logger) : ICommandHandler<ResetPassword>
{
    private const string Expired = "This link has expired or has already been used. Ask for a new one from \"Forgot password?\".";

    public async ValueTask<Unit> Handle(ResetPassword command, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(command.UserId) ?? throw new BusinessRuleException(Expired);
        // Also sets a first password on an account made at checkout without one.
        var reset = await users.ResetPasswordAsync(user, command.Token, command.NewPassword);
        if (!reset.Succeeded)
        {
            if (reset.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
                throw new BusinessRuleException(Expired);
            throw new RequestValidationException(nameof(ResetPassword.NewPassword), string.Join(" ", reset.Errors.Select(e => e.Description)));
        }

        // The link proved they own the address; and too many wrong guesses before shouldn't keep them out now.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            (await users.UpdateAsync(user)).ThrowIfFailed();
        }
        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        logger.LogInformation("Password reset for user {UserId}", user.Id);
        return Unit.Value;
    }
}
