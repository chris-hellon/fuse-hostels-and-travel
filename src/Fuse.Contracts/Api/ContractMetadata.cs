using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Fuse.Contracts.Api;

/// <summary>Route + access metadata for a request contract, read once per type and cached.</summary>
public sealed partial class ContractMetadata
{
    public const string RoutePrefix = "api/app";

    private static readonly ConcurrentDictionary<Type, ContractMetadata> Cache = new();

    private ContractMetadata(Type type)
    {
        Type = type;
        Route = type.GetCustomAttribute<ApiRouteAttribute>(inherit: false);
        var access = type.GetCustomAttributes<AccessAttribute>(inherit: false).ToArray();
        Access = access.Length == 1 ? access[0] : null;
        AccessAttributeCount = access.Length;
        RouteParameters = Route is null
            ? []
            : [.. RouteParameterRegex().Matches(Route.Template).Select(m => m.Groups[1].Value)];
    }

    public Type Type { get; }
    public ApiRouteAttribute? Route { get; }
    public AccessAttribute? Access { get; }
    public int AccessAttributeCount { get; }
    public IReadOnlyList<string> RouteParameters { get; }

    [MemberNotNullWhen(true, nameof(Route))]
    public bool IsExposed => Route is not null;

    public static ContractMetadata For(Type type) => Cache.GetOrAdd(type, static t => new ContractMetadata(t));

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)(?::[^}]*)?\}")]
    private static partial Regex RouteParameterRegex();
}
