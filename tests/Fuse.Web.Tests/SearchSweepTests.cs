using System.Net;
using System.Text.Json;
using Shouldly;

namespace Fuse.Web.Tests;

/// <summary>
/// Every paged search, as an admin, sorted by every column of its row type in both directions.
/// Catches queries that only fail once sorted (e.g. EF can't translate the projection).
/// </summary>
public sealed class SearchSweepTests(TestApp app)
{
    public static TheoryData<string, string> Sorts()
    {
        var data = new TheoryData<string, string>();
        foreach (var contract in Contracts.All)
        {
            if (Contracts.PagedRowType(contract.Type) is not { } row)
                continue;
            data.Add(contract.Type.Name, "");
            foreach (var property in Contracts.SortableProperties(row))
            {
                data.Add(contract.Type.Name, property);
                data.Add(contract.Type.Name, property + " desc");
            }
        }
        return data;
    }

    [Theory, MemberData(nameof(Sorts))]
    public async Task Search_succeeds_for_every_sort(string name, string sort)
    {
        var admin = await app.SignedInAsync(TestApp.AdminEmail, staffOnly: true);
        var body = JsonSerializer.Serialize(new { pageNumber = 1, pageSize = 5, keyword = "a", orderBy = sort == "" ? null : new[] { sort } });

        var response = await admin.SendAsync(Contracts.Request(Contracts.Named(name), body), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
