using Microsoft.AspNetCore.Identity;
using Fuse.Contracts.Identity;
using Fuse.Domain.Identity;

namespace Fuse.Application.Features.Identity;

internal sealed class GetMyProfileHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<GetMyProfile, UserDetails>
{
    public async ValueTask<UserDetails> Handle(GetMyProfile query, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId?.ToString() ?? throw new UnauthorizedException();
        return await db.Users.AsNoTracking().Where(u => u.Id == userId).ToDetails().FirstOrDefaultAsync(cancellationToken)
               ?? throw new NotFoundException("Your account could not be found.");
    }
}

internal sealed class UpdateMyProfileHandler(UserManager<ApplicationUser> users, ICurrentUser currentUser) : ICommandHandler<UpdateMyProfile>
{
    public async ValueTask<Unit> Handle(UpdateMyProfile command, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(currentUser.UserId?.ToString() ?? throw new UnauthorizedException())
                   ?? throw new NotFoundException("Your account could not be found.");
        // A guest's email changes only through a link sent to the new address (RequestMyEmailChange): otherwise anyone
        // could claim an address they don't own, and bookings made with it at checkout would join their account.
        var profile = FuseRoles.StaffRoles.Any(currentUser.IsInRole)
            ? command.Profile
            : command.Profile with { Email = PlaceholderEmails.Is(user.Email) ? null : user.Email };
        user.Apply(profile);
        (await users.UpdateAsync(user)).ThrowIfFailed();
        return Unit.Value;
    }
}

internal sealed class ChangeMyPasswordHandler(UserManager<ApplicationUser> users, ICurrentUser currentUser) : ICommandHandler<ChangeMyPassword>
{
    public async ValueTask<Unit> Handle(ChangeMyPassword command, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(currentUser.UserId?.ToString() ?? throw new UnauthorizedException())
                   ?? throw new NotFoundException("Your account could not be found.");

        // Unlike the legacy version, the current password must be right.
        var result = await users.ChangePasswordAsync(user, command.CurrentPassword, command.NewPassword);
        if (!result.Succeeded && result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
            throw new RequestValidationException(nameof(ChangeMyPassword.CurrentPassword), "Your current password is incorrect.");
        result.ThrowIfFailed(nameof(ChangeMyPassword.NewPassword));
        return Unit.Value;
    }
}
