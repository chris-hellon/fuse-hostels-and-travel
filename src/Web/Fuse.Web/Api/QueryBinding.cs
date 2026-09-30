using System.Text.Json;
using System.Text.Json.Nodes;
using Fuse.Contracts.Api;

namespace Fuse.Web.Api;

/// <summary>
/// Builds a request contract from the HTTP request: the JSON body (POST/PUT) with route values
/// overlaid, or route values + query string (GET/DELETE). Everything goes through the shared
/// <see cref="ApiJson"/> options, so binding matches what <c>HttpApi</c> sent.
/// </summary>
public static class QueryBinding
{
    public static async ValueTask<object> BindAsync(HttpContext context, ContractMetadata contract)
    {
        JsonObject json;
        if (contract.Route!.HasBody && context.Request.ContentLength is not 0)
        {
            var node = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            json = node as JsonObject ?? throw new BadHttpRequestException("Request body must be a JSON object.");
        }
        else
        {
            json = [];
        }

        var properties = contract.Type.GetProperties().ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        if (!contract.Route.HasBody)
        {
            foreach (var (key, values) in context.Request.Query)
            {
                if (properties.TryGetValue(key, out var property))
                    json[property.Name] = ToNode(property.PropertyType, values.ToArray());
            }
        }

        // Route values always win over anything in the body or query.
        foreach (var name in contract.RouteParameters)
        {
            if (context.GetRouteValue(name) is { } value && properties.TryGetValue(name, out var property))
                json[property.Name] = ToNode(property.PropertyType, [value.ToString()]);
        }

        return json.Deserialize(contract.Type, ApiJson.Options)
            ?? throw new BadHttpRequestException("Request could not be read.");
    }

    public static bool IsBindableFromQuery(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (IsScalar(type))
            return true;
        var element = ElementType(type);
        return element is not null && IsScalar(Nullable.GetUnderlyingType(element) ?? element);
    }

    private static JsonNode? ToNode(Type type, string?[] values)
    {
        var element = ElementType(type);
        if (element is not null && type != typeof(string))
            return new JsonArray([.. values.Select(v => Scalar(element, v))]);
        return Scalar(type, values.LastOrDefault());
    }

    private static JsonNode? Scalar(Type type, string? value)
    {
        if (value is null)
            return null;
        type = Nullable.GetUnderlyingType(type) ?? type;
        // Numbers and enums are read from strings by ApiJson; only booleans need converting.
        return type == typeof(bool) ? JsonValue.Create(bool.Parse(value)) : JsonValue.Create(value);
    }

    private static bool IsScalar(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) ||
        type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly) ||
        type == typeof(TimeSpan);

    private static Type? ElementType(Type type)
    {
        if (type == typeof(string))
            return null;
        if (type.IsArray)
            return type.GetElementType();
        return type.GetInterfaces().Append(type)
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }
}
