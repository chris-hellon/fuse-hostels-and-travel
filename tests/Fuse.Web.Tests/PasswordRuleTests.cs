using System.Net;
using System.Net.Http.Json;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Fuse.Contracts.Auth;
using Fuse.Contracts.Common;
using Fuse.Contracts.Identity;
using Fuse.Contracts.Site;

namespace Fuse.Web.Tests;

/// <summary>Every password set anywhere follows legacy's rule: 6 to 100 characters with a lowercase letter.</summary>
public sealed class PasswordRuleTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly UserProfile Profile = new() { FirstName = "Pat", LastName = "Word", Email = "pat@tests.invalid" };

    /// <summary>Each form's validator, given one password (and its confirmation).</summary>
    public static TheoryData<string> Forms => ["staff", "supplier", "set staff", "change mine", "set mine", "reset", "register", "checkout"];

    private static bool Accepts(string form, string password) => form switch
    {
        "staff" => new CreateStaffValidator().Validate(new CreateStaff(Profile, password, password)).IsValid,
        "supplier" => new CreateSupplierValidator().Validate(new CreateSupplier(Profile, password, password)).IsValid,
        "set staff" => new SetStaffPasswordValidator().Validate(new SetStaffPassword("id", password, password)).IsValid,
        "change mine" => new ChangeMyPasswordValidator().Validate(new ChangeMyPassword("current", password, password)).IsValid,
        "set mine" => new SetMyPasswordValidator().Validate(new SetMyPassword(password, password)).IsValid,
        "reset" => new ResetPasswordValidator().Validate(new ResetPassword("id", "token", password, password)).IsValid,
        "register" => new RegisterGuestRequestValidator().Validate(new RegisterGuestRequest
        {
            FirstName = "Pat", Surname = "Word", Email = "pat@tests.invalid", DateOfBirth = new DateTime(1990, 1, 1), PhoneNumber = "1",
            Nationality = "VN", Gender = "N/A", Password = password, ConfirmPassword = password,
        }).IsValid,
        "checkout" => new PlaceBookingValidator().Validate(new PlaceBooking(
                new CheckoutContact { FirstName = "Pat", LastName = "Word", Email = "pat@tests.invalid" }, [],
                [new CheckoutTour { DepartureId = DefaultIdType.NewGuid() }], password, password, null))
            .Errors.All(e => e.PropertyName != nameof(PlaceBooking.Password)),
        _ => throw new ArgumentOutOfRangeException(nameof(form)),
    };

    [Theory, MemberData(nameof(Forms))]
    public void Every_form_needs_six_characters_and_a_lowercase_letter(string form)
    {
        Accepts(form, "abcdef").ShouldBeTrue();
        Accepts(form, "Pass1a").ShouldBeTrue();
        Accepts(form, "abcde").ShouldBeFalse();     // too short (legacy's forms: 6)
        Accepts(form, "ABCDEF12").ShouldBeFalse();  // no lowercase (legacy's Identity options)
        Accepts(form, new string('a', 101)).ShouldBeFalse();
    }

    [Fact]
    public void The_message_matches_legacys()
    {
        var error = new SetMyPasswordValidator().Validate(new SetMyPassword("abc", "abc")).Errors.ShouldHaveSingleItem();
        error.ErrorMessage.ShouldBe("The New password must be at least 6 and at max 100 characters long.");
    }

    [Fact]
    public async Task Identity_has_the_same_rule_so_nothing_slips_past_the_forms()
    {
        var options = app.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.Password;
        (options.RequiredLength, options.RequireLowercase, options.RequireUppercase, options.RequireDigit, options.RequireNonAlphanumeric, options.RequiredUniqueChars)
            .ShouldBe((6, true, false, false, false, 1));

        var api = await app.AdminApiAsync();
        var id = await api.Send(new CreateStaff(new UserProfile { FirstName = "Pat", LastName = "Word", Email = $"pat-{Guid.NewGuid():N}@tests.invalid" },
            TestApp.Password, TestApp.Password), Ct);
        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SetStaffPassword(id, "abcde", "abcde"), Ct).AsTask());

        var register = await app.Anonymous().PostAsJsonAsync(AuthRoutes.Register, new RegisterGuestRequest
        {
            FirstName = "Pat", Surname = "Word", Email = $"pat-{Guid.NewGuid():N}@tests.invalid", DateOfBirth = new DateTime(1990, 1, 1),
            PhoneNumber = "1", Nationality = "VN", Gender = "N/A", Password = "abcd", ConfirmPassword = "abcd",
        }, Ct);
        register.IsSuccessStatusCode.ShouldBeFalse();
        register.StatusCode.ShouldNotBe(HttpStatusCode.InternalServerError);
    }
}
