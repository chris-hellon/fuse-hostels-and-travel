using Shouldly;
using Fuse.Contracts.Identity;

namespace Fuse.Web.Tests;

/// <summary>Guests added at the desk may have no email (accounts get a hidden placeholder, as legacy).</summary>
public sealed class GuestWithoutEmailTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_guest_can_be_added_without_an_email_and_it_reads_back_empty()
    {
        var api = await app.AdminApiAsync();

        var id = await api.Send(new CreateGuest(new UserProfile { FirstName = "No", LastName = "Email" }), Ct);

        (await api.Send(new GetGuest(id), Ct)).Profile.Email.ShouldBeNull();

        // Editing them without an email keeps it that way; giving them one sets it.
        await api.Send(new UpdateGuest(id, new UserProfile { FirstName = "Still", LastName = "Nothing" }), Ct);
        (await api.Send(new GetGuest(id), Ct)).Profile.Email.ShouldBeNull();
        var email = $"found-{Guid.NewGuid():N}@tests.invalid";
        await api.Send(new UpdateGuest(id, new UserProfile { FirstName = "Now", LastName = "Found", Email = email }), Ct);
        (await api.Send(new GetGuest(id), Ct)).Profile.Email.ShouldBe(email);
    }

    [Fact]
    public async Task Two_guests_without_emails_can_both_be_added()
    {
        var api = await app.AdminApiAsync();

        var first = await api.Send(new CreateGuest(new UserProfile { FirstName = "A", LastName = "One" }), Ct);
        var second = await api.Send(new CreateGuest(new UserProfile { FirstName = "B", LastName = "Two" }), Ct);

        first.ShouldNotBe(second);
    }
}
