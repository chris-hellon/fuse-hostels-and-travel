using System.Reflection;
using Mediator;
using Fuse.Contracts.Api;

namespace Fuse.Web.Api;

/// <summary>
/// Every request contract in <c>Fuse.Contracts</c>, validated once at startup. A contract
/// without a route, without exactly one access attribute, or with a clashing route stops the host,
/// so an unprotected endpoint can't ship by omission.
/// </summary>
public static class ContractCatalog
{
    public static IReadOnlyList<ContractMetadata> Load(Assembly contractsAssembly)
    {
        var contracts = contractsAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IMessage).IsAssignableFrom(t))
            .Select(ContractMetadata.For)
            .ToList();

        var problems = new List<string>();
        foreach (var contract in contracts)
        {
            if (!contract.IsExposed)
                problems.Add($"{contract.Type.Name} has no route attribute ([Get]/[Post]/[Put]/[Delete]).");
            if (contract.AccessAttributeCount != 1)
                problems.Add($"{contract.Type.Name} must have exactly one access attribute ([Public]/[Authenticated]/[Staff]/[Permission]); found {contract.AccessAttributeCount}.");
            if (contract.IsExposed && !contract.Route.HasBody)
            {
                var bodyOnly = contract.Type.GetProperties()
                    .Where(p => p.CanWrite && !QueryBinding.IsBindableFromQuery(p.PropertyType))
                    .Select(p => p.Name)
                    .ToList();
                if (bodyOnly.Count > 0)
                    problems.Add($"{contract.Type.Name} is {contract.Route.Method} but has complex properties ({string.Join(", ", bodyOnly)}); use [Post].");
            }
        }

        foreach (var clash in contracts.Where(c => c.IsExposed)
                     .GroupBy(c => $"{c.Route!.Method} {c.Route.Template.ToLowerInvariant()}")
                     .Where(g => g.Count() > 1))
        {
            problems.Add($"Route clash on {clash.Key}: {string.Join(", ", clash.Select(c => c.Type.Name))}.");
        }

        if (problems.Count > 0)
            throw new InvalidOperationException("Invalid request contracts:\n  " + string.Join("\n  ", problems));

        return contracts;
    }
}
