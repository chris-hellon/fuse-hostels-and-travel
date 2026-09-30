using Fuse.Application.Common.Files;
using Fuse.Contracts.Identity;
using Fuse.Domain.Identity;

namespace Fuse.Application.Features.Identity;

// Legacy's profile photo (ApplicationUser.ImageUrl): Upload replaces it, Delete empties it. Stored like every other
// upload (tenant container, assets/images/{guid}.ext); the old file stays in storage, as legacy (never deleted).

internal static class ProfileImages
{
    /// <summary>Saves the photo (or removes it) on a tracked user; returns the new URL, empty when removed (as legacy).</summary>
    public static async Task<string> SetAsync(IAppDbContext db, IFileStore files, ApplicationUser user, FileUpload? image, CancellationToken cancellationToken)
    {
        var saved = image is null ? null : await files.SaveAsync(image, FileKind.Image, cancellationToken);
        user.ImageUrl = saved ?? string.Empty;
        try
        {
            // Straight to the table, as legacy: only the photo changes, so there's nothing for Identity to validate.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await files.DeleteAsync(saved, CancellationToken.None);
            throw;
        }
        return user.ImageUrl;
    }
}

internal sealed class SetStaffImageHandler(UserKinds kinds, IAppDbContext db, IFileStore files) : ICommandHandler<SetStaffImage, string>
{
    public async ValueTask<string> Handle(SetStaffImage command, CancellationToken cancellationToken) =>
        await ProfileImages.SetAsync(db, files, await kinds.FindAsync(command.Id, staff: true, cancellationToken), command.Image, cancellationToken);
}

internal sealed class SetMyImageHandler(IAppDbContext db, IFileStore files, ICurrentUser currentUser) : ICommandHandler<SetMyImage, string>
{
    public async ValueTask<string> Handle(SetMyImage command, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId?.ToString() ?? throw new UnauthorizedException();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                   ?? throw new NotFoundException("Your account could not be found.");
        return await ProfileImages.SetAsync(db, files, user, command.Image, cancellationToken);
    }
}
