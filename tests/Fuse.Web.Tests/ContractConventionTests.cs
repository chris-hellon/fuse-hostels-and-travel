using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Fuse.Web.Tests;

public sealed class ContractConventionTests(TestApp app)
{
    [Fact]
    public void Contracts_are_discovered() => Contracts.All.Count.ShouldBeGreaterThan(10);

    [Fact]
    public void Every_contract_has_a_route_and_one_access_rule() =>
        // ContractCatalog.Load throws on any violation; this makes it a build failure, not a startup one.
        Contracts.All.ShouldAllBe(c => c.IsExposed && c.AccessAttributeCount == 1);

    public static TheoryData<string> AllContracts => Contracts.Names(_ => true);

    [Theory, MemberData(nameof(AllContracts))]
    public void Every_contract_has_a_handler(string name)
    {
        var type = Contracts.Named(name).Type;
        var response = Contracts.ResponseType(type);
        var handler = response is null
            ? typeof(ICommandHandler<>).MakeGenericType(type)
            : type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>))
                ? typeof(IQueryHandler<,>).MakeGenericType(type, response)
                : typeof(ICommandHandler<,>).MakeGenericType(type, response);

        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetService(handler).ShouldNotBeNull($"{name} has no {handler.Name} registered.");
    }

    [Fact]
    public void Every_table_type_and_computed_column_copies_from_the_dev_database()
    {
        app.ProcedureFailures.ShouldBeEmpty();
        TestContext.Current.TestOutputHelper?.WriteLine($"Skipped procedures:\n{string.Join("\n", app.SkippedProcedures)}");
    }
}
