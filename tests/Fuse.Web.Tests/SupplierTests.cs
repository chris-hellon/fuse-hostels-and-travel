using Shouldly;
using Fuse.Contracts.Common;
using Fuse.Contracts.Identity;

namespace Fuse.Web.Tests;

public sealed class SupplierTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_supplier_is_listed_under_suppliers()
    {
        var api = await app.AdminApiAsync();
        var email = $"supplier-{Guid.NewGuid():N}@tests.invalid";

        var id = await api.Send(new CreateSupplier(new UserProfile { FirstName = "Sam", LastName = "Supplier", Email = email, City = "Hoi An" }, TestApp.Password, TestApp.Password), Ct);

        var row = (await api.Send(new SearchSuppliers { Keyword = "Sam", PageSize = 100 }, Ct)).Items.Where(u => u.Id == id).ShouldHaveSingleItem();
        row.City.ShouldBe("Hoi An");
        (await api.Send(new SearchGuests { Keyword = "Sam", PageSize = 100 }, Ct)).Items.ShouldNotContain(u => u.Id == id);
    }

    [Fact]
    public async Task An_administrator_can_set_a_suppliers_password_and_edit_their_details()
    {
        var api = await app.AdminApiAsync();
        var email = $"supplier-{Guid.NewGuid():N}@tests.invalid";
        var id = await api.Send(new CreateSupplier(new UserProfile { FirstName = "Sam", LastName = "Supplier", Email = email, Nationality = "VN" }, TestApp.Password, TestApp.Password), Ct);

        await Should.ThrowAsync<RequestValidationException>(() => api.Send(new SetStaffPassword(id, "abcd", "abce"), Ct).AsTask());
        await api.Send(new SetStaffPassword(id, TestApp.Password, TestApp.Password), Ct);
        await app.SignedInAsync(email); // signs in with the new password

        var details = await api.Send(new GetStaff(id), Ct);
        await api.Send(new UpdateStaff(id, details.Profile with { City = "Hoi An", PhoneNumber = "0905" }), Ct);
        var saved = await api.Send(new GetStaff(id), Ct);
        (saved.Profile.City, saved.Profile.PhoneNumber, saved.Profile.Nationality).ShouldBe(("Hoi An", "0905", "VN"));
    }
}
