using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Fuse.Contracts.Api;
using Fuse.Contracts.Auth;

namespace Fuse.Web.Tests;

public sealed class AuthTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Wrong_password_gets_the_generic_message()
    {
        // Own account: wrong passwords count towards lockout, so never aim them at the shared admin.
        var email = $"wrong-{Guid.NewGuid():N}@example.invalid";
        (await app.Anonymous().PostAsJsonAsync(AuthRoutes.Register, new RegisterGuestRequest
        {
            FirstName = "Wrong", Surname = "Password", Email = email, DateOfBirth = new DateTime(1990, 1, 1),
            PhoneNumber = "1", Nationality = "VN", Gender = "N/A", Password = "abcdef", ConfirmPassword = "abcdef",
        }, Ct)).EnsureSuccessStatusCode();

        var response = await app.Anonymous().PostAsJsonAsync(AuthRoutes.SignIn,
            new SignInRequest { Email = email, Password = "wrong" }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("email or password is incorrect");
    }

    [Fact]
    public async Task Guests_cannot_use_the_staff_sign_in()
    {
        var response = await app.Anonymous().PostAsJsonAsync(AuthRoutes.SignIn,
            new SignInRequest { Email = TestApp.GuestEmail, Password = TestApp.Password, StaffOnly = true }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_gets_permission_claims_without_a_bloated_cookie()
    {
        var admin = await app.SignedInAsync(TestApp.AdminEmail, staffOnly: true);
        var me = await admin.GetFromJsonAsync<CurrentUserInfo>(AuthRoutes.Me, ApiJson.Options, Ct);
        me!.IsAuthenticated.ShouldBeTrue();
        me.Claims.Count(c => c.Type == "permission").ShouldBeGreaterThan(100);
    }

    [Fact]
    public async Task Registering_an_existing_email_is_a_conflict()
    {
        var response = await app.Anonymous().PostAsJsonAsync(AuthRoutes.Register, new RegisterGuestRequest
        {
            FirstName = "Dup", Surname = "Licate", Email = TestApp.GuestEmail, DateOfBirth = new DateTime(1990, 1, 1),
            PhoneNumber = "1", Nationality = "VN", Gender = "N/A", Password = "abcdef", ConfirmPassword = "abcdef",
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_dev_sign_in_only_exists_in_development()
    {
        var response = await app.Anonymous().GetAsync("/dev/sign-in", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
    }
}
