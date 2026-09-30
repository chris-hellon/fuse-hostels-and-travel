using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Contracts.Auth;
using Fuse.Contracts.Common;
using Fuse.Contracts.Identity;
using Fuse.Domain.Identity;
using Fuse.Shared.Authorization;

namespace Fuse.Web.Tests;

/// <summary>Forgot password: a single-use emailed link, for guests (website) and staff (back office).</summary>
public sealed partial class PasswordResetTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private Fuse.Web.Client.Api.HttpApi Anonymous => new(app.Anonymous());

    /// <summary>A new user in the role, with no password (as a guest made at checkout) unless one is given.</summary>
    private async Task<string> UserAsync(string role, string? password = null)
    {
        var email = $"reset-{Guid.NewGuid():N}@tests.invalid";
        await using var scope = TestBookings.TenantScope(app);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, FirstName = "Robin", LastName = "Reset", FullName = "Robin Reset", IsActive = true };
        (password is null ? await users.CreateAsync(user) : await users.CreateAsync(user, password)).Succeeded.ShouldBeTrue();
        await users.AddToRoleAsync(user, role);
        return email;
    }

    private (string Path, string UserId, string Token)? SentLink(string email)
    {
        var sent = app.Emails.Sent.LastOrDefault(m => m.To.Contains(email) && m.Subject == "Reset your password");
        if (sent is null)
            return null;
        var link = new Uri(WebUtility.HtmlDecode(Link().Match(sent.HtmlBody).Groups[1].Value));
        var query = System.Web.HttpUtility.ParseQueryString(link.Query);
        return (link.AbsolutePath, query["user"]!, query["token"]!);
    }

    private async Task<bool> SignsInAsync(string email, string password, bool staff = false) =>
        (await app.Anonymous().PostAsJsonAsync(AuthRoutes.SignIn, new SignInRequest { Email = email, Password = password, StaffOnly = staff }, Ct)).IsSuccessStatusCode;

    [Fact]
    public async Task A_guest_without_a_password_sets_one_from_the_emailed_link_which_works_once()
    {
        var email = await UserAsync(FuseRoles.Guest);

        await Anonymous.Send(new RequestPasswordReset(email), Ct);

        var (path, userId, token) = SentLink(email).ShouldNotBeNull();
        path.ShouldBe("/reset-password");
        await Should.ThrowAsync<BusinessRuleException>(() => Anonymous.Send(new ResetPassword(userId, "forged", "newpass", "newpass"), Ct).AsTask());
        await Should.ThrowAsync<RequestValidationException>(() => Anonymous.Send(new ResetPassword(userId, token, "newpass", "other"), Ct).AsTask());

        await Anonymous.Send(new ResetPassword(userId, token, "newpass", "newpass"), Ct);

        (await SignsInAsync(email, "newpass")).ShouldBeTrue();
        await Should.ThrowAsync<BusinessRuleException>(() => Anonymous.Send(new ResetPassword(userId, token, "again1", "again1"), Ct).AsTask()); // used
    }

    [Fact]
    public async Task Staff_get_a_back_office_link_and_a_reset_unlocks_a_locked_account()
    {
        var email = await UserAsync(FuseRoles.Basic, "oldpass");
        for (var i = 0; i < 6; i++)
            await SignsInAsync(email, "wrong", staff: true);
        (await SignsInAsync(email, "oldpass", staff: true)).ShouldBeFalse(); // locked out

        await Anonymous.Send(new RequestPasswordReset(email, Staff: true), Ct);
        var (path, userId, token) = SentLink(email).ShouldNotBeNull();
        path.ShouldBe("/admin/reset-password");
        await Anonymous.Send(new ResetPassword(userId, token, "newpass", "newpass"), Ct);

        (await SignsInAsync(email, "newpass", staff: true)).ShouldBeTrue();
        (await SignsInAsync(email, "oldpass", staff: true)).ShouldBeFalse();
    }

    [Fact]
    public async Task Asking_never_reveals_whether_an_email_has_an_account()
    {
        var unknown = $"nobody-{Guid.NewGuid():N}@tests.invalid";
        var guest = await UserAsync(FuseRoles.Guest);

        await Anonymous.Send(new RequestPasswordReset(unknown), Ct); // no error
        await Anonymous.Send(new RequestPasswordReset(guest, Staff: true), Ct); // a guest on the back office: no error, no email

        SentLink(unknown).ShouldBeNull();
        SentLink(guest).ShouldBeNull();
        await Should.ThrowAsync<RequestValidationException>(() => Anonymous.Send(new RequestPasswordReset("not an email"), Ct).AsTask());
    }

    [Theory]
    [InlineData("/admin/forgot-password", "Forgot Password?")]
    [InlineData("/admin/reset-password?user=x&token=y", "Reset Password")]
    [InlineData("/reset-password?user=x&token=y", "RESET PASSWORD")]
    public async Task The_pages_load_signed_out(string path, string heading)
    {
        var response = await app.Anonymous().GetAsync(path, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        // Admin pages render in the browser (the server sends their loading screen); the website's is prerendered.
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain(path.StartsWith("/admin") ? "admin-boot" : heading);
    }

    [GeneratedRegex("href=\"([^\"]*reset-password[^\"]*)\"")]
    private static partial Regex Link();
}
