using System.Net;
using Shouldly;
using Fuse.Contracts.Api;

namespace Fuse.Web.Tests;

/// <summary>The access attribute on each contract is actually enforced over HTTP.</summary>
public sealed class AccessTests(TestApp app)
{
    public static TheoryData<string> Protected => Contracts.Names(c => c.Access is not PublicAttribute);
    public static TheoryData<string> StaffOnly => Contracts.Names(c => c.Access is PermissionAttribute or StaffAttribute);

    [Theory, MemberData(nameof(Protected))]
    public async Task Anonymous_callers_get_401(string name)
    {
        var response = await app.Anonymous().SendAsync(Contracts.Request(Contracts.Named(name)), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory, MemberData(nameof(StaffOnly))]
    public async Task Guests_get_403_from_staff_endpoints(string name)
    {
        var guest = await app.SignedInAsync(TestApp.GuestEmail);
        var response = await guest.SendAsync(Contracts.Request(Contracts.Named(name)), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task State_changing_calls_without_the_marker_header_are_rejected()
    {
        var command = Contracts.All.First(c => c.Route!.Method != "GET");
        var admin = await app.SignedInAsync(TestApp.AdminEmail);
        var request = Contracts.Request(command);
        admin.DefaultRequestHeaders.Remove(ApiHeaders.Marker);
        var response = await admin.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
