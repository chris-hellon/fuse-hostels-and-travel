using System.Reflection;
using Mediator;
using Fuse.Contracts.Api;
using Fuse.Contracts.Common;
using Fuse.Web.Api;

namespace Fuse.Web.Tests;

/// <summary>Every request contract, as the host sees it; tests enumerate these so new screens are covered automatically.</summary>
public static class Contracts
{
    public static IReadOnlyList<ContractMetadata> All { get; } = ContractCatalog.Load(typeof(ContractMetadata).Assembly);

    public static TheoryData<string> Names(Func<ContractMetadata, bool> filter) =>
        new(All.Where(filter).Select(c => c.Type.Name));

    public static ContractMetadata Named(string name) => All.Single(c => c.Type.Name == name);

    /// <summary>The URL with every route parameter filled with a dummy id.</summary>
    public static string Url(ContractMetadata contract) =>
        ContractMetadata.RoutePrefix + "/" + System.Text.RegularExpressions.Regex.Replace(contract.Route!.Template, @"\{[^}]+\}", Guid.NewGuid().ToString());

    public static HttpRequestMessage Request(ContractMetadata contract, string? body = null) => new(new HttpMethod(contract.Route!.Method), Url(contract))
    {
        Content = contract.Route.HasBody ? new StringContent(body ?? "{}", System.Text.Encoding.UTF8, "application/json") : null,
    };

    /// <summary>The TResponse of IQuery&lt;TResponse&gt; / ICommand&lt;TResponse&gt;, or null for void commands.</summary>
    public static Type? ResponseType(Type contract) => contract.GetInterfaces()
        .FirstOrDefault(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IQuery<>) || i.GetGenericTypeDefinition() == typeof(ICommand<>)))
        ?.GetGenericArguments()[0];

    /// <summary>For paged searches: the row type T of PagedResult&lt;T&gt;.</summary>
    public static Type? PagedRowType(Type contract) =>
        typeof(PagedQuery).IsAssignableFrom(contract) && ResponseType(contract) is { IsGenericType: true } response
        && response.GetGenericTypeDefinition() == typeof(PagedResult<>)
            ? response.GetGenericArguments()[0]
            : null;

    public static IEnumerable<string> SortableProperties(Type row) => row
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) is var t
                    && (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(Guid) || t == typeof(DateTime) || t == typeof(DateOnly)))
        .Select(p => p.Name);
}
