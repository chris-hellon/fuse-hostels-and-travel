using Shouldly;
using Fuse.Contracts.Common;
using Fuse.Contracts.Identity;

namespace Fuse.Web.Tests;

/// <summary>Legacy's profile photo (ApplicationUser.ImageUrl): uploaded, replaced and removed from the staff profile and My Account.</summary>
public sealed class ProfilePhotoTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FileUpload Png(string name = "me.png") => new(name, "image/png", [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);

    [Fact]
    public async Task An_administrator_can_upload_replace_and_remove_a_staff_members_photo()
    {
        var api = await app.AdminApiAsync();
        var email = $"photo-{Guid.NewGuid():N}@tests.invalid";
        var id = await api.Send(new CreateStaff(new UserProfile { FirstName = "Pho", LastName = "To", Email = email }, TestApp.Password, TestApp.Password), Ct);

        var first = await api.Send(new SetStaffImage(id, Png()), Ct);
        first.ShouldEndWith(".png");
        first.ShouldContain("assets/images/"); // stored like every other upload, as legacy
        (await api.Send(new GetStaff(id), Ct)).ImageUrl.ShouldBe(first);

        var second = await api.Send(new SetStaffImage(id, Png("other.png")), Ct);
        second.ShouldNotBe(first);
        (await api.Send(new GetStaff(id), Ct)).ImageUrl.ShouldBe(second);

        // Removed as legacy: the URL is emptied (the file itself stays in storage).
        (await api.Send(new SetStaffImage(id, null), Ct)).ShouldBeEmpty();
        (await api.Send(new GetStaff(id), Ct)).ImageUrl.ShouldBeEmpty();

        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SetStaffImage(id, new FileUpload("notes.txt", "text/plain", [1])), Ct).AsTask());
    }

    [Fact]
    public async Task Staff_set_their_own_photo_and_it_goes_in_the_sign_in_claims()
    {
        var api = await app.AdminApiAsync();
        var email = $"photo-{Guid.NewGuid():N}@tests.invalid";
        await api.Send(new CreateStaff(new UserProfile { FirstName = "Pho", LastName = "To", Email = email }, TestApp.Password, TestApp.Password), Ct);
        var me = new Fuse.Web.Client.Api.HttpApi(await app.SignedInAsync(email, staffOnly: true));

        var url = await me.Send(new SetMyImage(Png()), Ct);
        (await me.Send(new GetMyProfile(), Ct)).ImageUrl.ShouldBe(url);

        var claims = await (await app.SignedInAsync(email, staffOnly: true)).GetStringAsync(Fuse.Contracts.Auth.AuthRoutes.Me, Ct);
        claims.ShouldContain(url);
    }

    [Fact]
    public async Task Guests_have_no_profile_photo()
    {
        var guest = new Fuse.Web.Client.Api.HttpApi(await app.SignedInAsync(TestApp.GuestEmail));
        await Should.ThrowAsync<ForbiddenException>(() => guest.Send(new SetMyImage(Png()), Ct).AsTask());
    }
}
