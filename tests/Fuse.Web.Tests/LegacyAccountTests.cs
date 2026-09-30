using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Contracts.Auth;
using Fuse.Contracts.Common;
using Fuse.Contracts.Identity;
using Fuse.Domain.Identity;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Authorization;

namespace Fuse.Web.Tests;

/// <summary>
/// Accounts as the legacy database has them: emails shared by several accounts (legacy wrote Cloudbeds guests straight
/// to the table) and staff whose username is an old email. They sign in, are edited and reset as they were in legacy.
/// </summary>
public sealed partial class LegacyAccountTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewEmail(string kind) => $"{kind}-{Guid.NewGuid():N}@tests.invalid";

    /// <summary>An account written as legacy's Cloudbeds import did: a GUID username and any email, bypassing Identity.</summary>
    private async Task<string> LegacyAccountAsync(string? email, string role = FuseRoles.Guest)
    {
        await using var scope = TestBookings.TenantScope(app);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = Guid.NewGuid().ToString(), Email = email, FirstName = "Lee", LastName = "Legacy", FullName = "Lee Legacy",
            IsActive = true, EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString(),
        };
        user.NormalizedUserName = users.NormalizeName(user.UserName);
        user.NormalizedEmail = email is null ? null : users.NormalizeEmail(email);
        var roleId = await db.Roles.Where(r => r.Name == role).Select(r => r.Id).SingleAsync(Ct);
        db.Users.Add(user);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roleId });
        await db.SaveChangesAsync(Ct);
        return user.Id;
    }

    private async Task<HttpResponseMessage> SignInAsync(string email, string password, bool staff) =>
        await app.Anonymous().PostAsJsonAsync(AuthRoutes.SignIn, new SignInRequest { Email = email, Password = password, StaffOnly = staff }, Ct);

    [Fact]
    public async Task Staff_sign_in_with_their_username_after_their_email_changes_and_with_the_new_one()
    {
        var api = await app.AdminApiAsync();
        var original = NewEmail("staff");
        var id = await api.Send(new CreateStaff(new UserProfile { FirstName = "Una", LastName = "Username", Email = original }, TestApp.Password, TestApp.Password), Ct);
        var changed = NewEmail("staff-new");
        var profile = (await api.Send(new GetStaff(id), Ct)).Profile;
        await api.Send(new UpdateStaff(id, profile with { Email = changed }), Ct);

        (await SignInAsync(original, TestApp.Password, staff: true)).StatusCode.ShouldBe(HttpStatusCode.OK); // as legacy
        (await SignInAsync(changed, TestApp.Password, staff: true)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Accounts_sharing_an_email_sign_in_are_edited_and_reset_as_in_legacy()
    {
        var api = await app.AdminApiAsync();
        var shared = NewEmail("shared");
        var staffId = await api.Send(new CreateStaff(new UserProfile { FirstName = "Sid", LastName = "Shared", Email = shared }, TestApp.Password, TestApp.Password), Ct);
        var guestId = await LegacyAccountAsync(shared);
        await LegacyAccountAsync(shared);

        // Sign-in (back office and website) finds the staff member, who signs in with that address.
        (await SignInAsync(shared, TestApp.Password, staff: true)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SignInAsync(shared, TestApp.Password, staff: false)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Everyone keeps the shared email through edits, deactivation and a new password.
        var staff = await api.Send(new GetStaff(staffId), Ct);
        await api.Send(new UpdateStaff(staffId, staff.Profile with { PhoneNumber = "0905 222" }), Ct);
        await api.Send(new SetStaffPassword(staffId, "newpass", "newpass"), Ct);
        await api.Send(new SetStaffActive(staffId, false), Ct);
        await api.Send(new SetStaffActive(staffId, true), Ct);
        var guest = await api.Send(new GetGuest(guestId), Ct);
        guest.Profile.Email.ShouldBe(shared);
        await api.Send(new UpdateGuest(guestId, guest.Profile with { PhoneNumber = "0905 333" }), Ct);
        (await api.Send(new GetGuest(guestId), Ct)).Profile.ShouldSatisfyAllConditions(
            p => p.Email.ShouldBe(shared), p => p.PhoneNumber.ShouldBe("0905 333"));

        // Forgot password doesn't fail on the shared address.
        await new Fuse.Web.Client.Api.HttpApi(app.Anonymous()).Send(new RequestPasswordReset(shared, Staff: true), Ct);
        await new Fuse.Web.Client.Api.HttpApi(app.Anonymous()).Send(new RequestPasswordReset(shared), Ct);
        app.Emails.Sent.Count(m => m.To.Contains(shared) && m.Subject == "Reset your password").ShouldBe(2);

        // But nobody can take an address someone else already has.
        var other = await api.Send(new CreateGuest(new UserProfile { FirstName = "Oli", LastName = "Other", Email = NewEmail("other") }), Ct);
        var otherProfile = (await api.Send(new GetGuest(other), Ct)).Profile;
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new UpdateGuest(other, otherProfile with { Email = shared }), Ct).AsTask());
        await Should.ThrowAsync<RequestValidationException>(() =>
            api.Send(new CreateGuest(new UserProfile { FirstName = "New", LastName = "Clash", Email = shared }), Ct).AsTask());
    }

    [Fact]
    public async Task A_legacy_guest_with_no_email_or_an_odd_username_can_still_be_edited()
    {
        var api = await app.AdminApiAsync();
        var guestId = await LegacyAccountAsync(null);
        await using (var scope = TestBookings.TenantScope(app))
        {
            // e.g. legacy's "N/A" and apostrophe usernames, which Identity wouldn't accept now.
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(u => u.Id == guestId).ExecuteUpdateAsync(u => u.SetProperty(x => x.UserName, $"o'{guestId}"), Ct);
        }

        var guest = await api.Send(new GetGuest(guestId), Ct);
        await api.Send(new UpdateGuest(guestId, guest.Profile with { PhoneNumber = "0905 444" }), Ct);

        (await api.Send(new GetGuest(guestId), Ct)).Profile.PhoneNumber.ShouldBe("0905 444");
    }

    [Fact]
    public async Task Administrators_send_staff_and_suppliers_a_password_reset_link_from_the_lists()
    {
        var api = await app.AdminApiAsync();
        var staffEmail = NewEmail("staff-reset");
        var staffId = await api.Send(new CreateStaff(new UserProfile { FirstName = "Rae", LastName = "Reset", Email = staffEmail }, TestApp.Password, TestApp.Password), Ct);
        var supplierEmail = NewEmail("supplier-reset");
        var supplierId = await api.Send(new CreateSupplier(new UserProfile { FirstName = "Sue", LastName = "Supplier", Email = supplierEmail }, TestApp.Password, TestApp.Password), Ct);

        await api.Send(new SendStaffPasswordReset(staffId), Ct);
        await api.Send(new SendSupplierPasswordReset(supplierId), Ct);
        await Should.ThrowAsync<NotFoundException>(() => api.Send(new SendSupplierPasswordReset(staffId), Ct).AsTask()); // not a supplier

        var (path, userId, token) = SentLink(staffEmail);
        path.ShouldBe("/admin/reset-password");
        userId.ShouldBe(staffId);
        await new Fuse.Web.Client.Api.HttpApi(app.Anonymous()).Send(new ResetPassword(userId, token, "fresh1", "fresh1"), Ct);
        (await SignInAsync(staffEmail, "fresh1", staff: true)).StatusCode.ShouldBe(HttpStatusCode.OK);

        SentLink(supplierEmail).UserId.ShouldBe(supplierId);
    }

    private (string Path, string UserId, string Token) SentLink(string email)
    {
        var sent = app.Emails.Sent.Where(m => m.To.Contains(email) && m.Subject == "Reset your password").ShouldHaveSingleItem();
        var link = new Uri(WebUtility.HtmlDecode(Link().Match(sent.HtmlBody).Groups[1].Value));
        var query = System.Web.HttpUtility.ParseQueryString(link.Query);
        return (link.AbsolutePath, query["user"]!, query["token"]!);
    }

    [GeneratedRegex("href=\"([^\"]*reset-password[^\"]*)\"")]
    private static partial Regex Link();
}
